using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Logging;
using Stx.Core.Queue;
using Stx.Look;
using Stx.Tools;

namespace Stx.ViewModels;

/// <summary>One row of the queue: what the job is doing, in plain words and on its LED and meter.</summary>
public sealed class QueueItemViewModel(long jobId, string title, string? pageUrl) : ObservableObject
{
    const int LogLinesKept = 2000;

    readonly Queue<string> _log = new();
    string _status = "QUEUED";
    double _percent;
    LedState _ledState = LedState.Off;
    LedColor _ledColor = LedColor.Amber;
    string _error = "", _next = "";
    JobState _state = JobState.Queued;

    public long JobId { get; } = jobId;
    public string Title { get; } = title;
    public string? PageUrl { get; } = pageUrl;
    public long MediaId { get; private set; }

    public JobState State { get => _state; private set => SetProperty(ref _state, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public double Percent { get => _percent; private set => SetProperty(ref _percent, value); }
    public LedState LedState { get => _ledState; private set => SetProperty(ref _ledState, value); }
    public LedColor LedColor { get => _ledColor; private set => SetProperty(ref _ledColor, value); }
    public string ErrorMessage { get => _error; private set => SetProperty(ref _error, value); }
    public string NextStep { get => _next; private set => SetProperty(ref _next, value); }

    public bool ShowsReopen => State == JobState.NeedsRecapture;
    public bool CanPause => JobTransitions.CanMove(State, JobState.Paused);
    public bool CanResume => State == JobState.Paused;
    public bool CanCancel => JobTransitions.CanMove(State, JobState.Canceled);
    public bool CanRetry => State is JobState.Failed or JobState.Canceled;

    public string RawLog
    {
        get { lock (_log) return string.Join('\n', _log); }
    }

    public void AddLog(string line)
    {
        string clean = Redactor.Scrub(line);
        lock (_log)
        {
            _log.Enqueue(clean);
            if (_log.Count > LogLinesKept) _log.Dequeue();
        }
    }

    public void Apply(Job job)
    {
        MediaId = job.MediaId;
        State = job.State;
        (LedColor, LedState) = job.State switch
        {
            JobState.Running => (LedColor.Amber, LedState.Strike),
            JobState.Analyzing or JobState.Waiting => (LedColor.Amber, LedState.Busy),
            JobState.Paused => (LedColor.Amber, LedState.On),
            JobState.Completed => (LedColor.Green, LedState.On),
            JobState.Failed => (LedColor.Red, LedState.On),
            JobState.NeedsRecapture => (LedColor.Blue, LedState.On),
            _ => (LedColor.Amber, LedState.Off),
        };
        if (job.State == JobState.Completed) Percent = 100;
        ErrorMessage = job.State is JobState.Failed or JobState.Waiting or JobState.NeedsRecapture && job.Error is { } kind ? kind.Message() : "";
        NextStep = job.State is JobState.Failed or JobState.NeedsRecapture && job.Error is { } next ? next.NextStep() : "";
        Status = job.State switch
        {
            JobState.Running => "STARTING",
            JobState.Paused => "PAUSED",
            JobState.Waiting => "RETRYING SHORTLY",
            JobState.Analyzing => "MEASURING",
            JobState.Completed => "COMPLETE",
            JobState.Failed => "FAILED",
            JobState.Canceled => "CANCELED",
            JobState.NeedsRecapture => "NEEDS RE-CAPTURE",
            _ => "QUEUED",
        };
        OnPropertyChanged(nameof(ShowsReopen));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRetry));
    }

    public void Show(Progress progress)
    {
        if (State != JobState.Running) return;
        Percent = progress.Percent;
        string detail = string.Join(" · ", new[] { progress.Speed, progress.Eta is null ? null : "ETA " + progress.Eta }.Where(s => s is not null));
        Status = detail.Length > 0 ? $"DOWNLOADING · {detail}" : "DOWNLOADING";
    }
}

/// <summary>The queue as you see it. It follows the engine and sends your actions back to it.</summary>
public sealed class QueueViewModel
{
    readonly QueueEngine _engine;
    readonly IMediaCatalog _catalog;
    readonly IShell _shell;
    readonly Action<Action> _ui;
    readonly Dictionary<long, long> _savedFile = new();

    public QueueViewModel(QueueEngine engine, IMediaCatalog catalog, IShell shell, Action<Action> ui)
    {
        _engine = engine;
        _catalog = catalog;
        _shell = shell;
        _ui = ui;
        engine.JobChanged += (_, job) => _ui(() => Update(job));
    }

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    /// <summary>True only while a retry is pending, so its timer can stop the rest of the time.</summary>
    public bool HasWaitingJobs => Items.Any(i => i.State == JobState.Waiting);

    /// <summary>Brings back what was in the queue when the app last closed.</summary>
    public void Load()
    {
        foreach (var job in _engine.AllJobs()) Update(job);
    }

    public void Pause(QueueItemViewModel item) => _engine.Pause(item.JobId);
    public void Resume(QueueItemViewModel item) => _engine.Resume(item.JobId);
    public void Cancel(QueueItemViewModel item) => _engine.Cancel(item.JobId);
    public void Retry(QueueItemViewModel item) => _engine.Retry(item.JobId);

    public void Reopen(QueueItemViewModel item)
    {
        if (item.PageUrl is { } url) _shell.OpenPage(url);
    }

    public void AddLog(long jobId, string line) => Find(jobId)?.AddLog(line);

    public void SetProgress(long jobId, Progress progress) => Find(jobId)?.Show(progress);

    /// <summary>A new file exists, and it's ready to be measured.</summary>
    public event EventHandler<SavedMedia>? FileRecorded;

    public void RecordFile(long jobId, string path)
    {
        if (Find(jobId) is not { } item) return;
        long fileId = _catalog.AddFile(item.MediaId, path, null, SourceViewModel.OriginalFormat);
        _savedFile[jobId] = fileId;
        FileRecorded?.Invoke(this, new SavedMedia(item.MediaId, fileId, path, item.Title));
    }

    QueueItemViewModel? Find(long jobId) => Items.FirstOrDefault(i => i.JobId == jobId);

    void Update(Job job)
    {
        var item = Find(job.Id);
        if (item is null)
        {
            var media = _catalog.GetItem(job.MediaId);
            item = new QueueItemViewModel(job.Id, media?.Title ?? media?.PageUrl ?? "Untitled", media?.PageUrl);
            Items.Add(item);
        }
        bool finishing = item.State != JobState.Completed && job.State == JobState.Completed;
        item.Apply(job);

        // A format chosen before the download is applied now, as its own job that keeps the original.
        if (finishing && ConversionPlan.AfterDownload(job.Preset) is { } output && _savedFile.TryGetValue(job.Id, out long fileId))
            _engine.Enqueue(job.MediaId, JobKind.Convert, new ConversionPlan(output, fileId, null).ToString());
    }
}

public sealed record SavedMedia(long MediaId, long FileId, string Path, string Title);
