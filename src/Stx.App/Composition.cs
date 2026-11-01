using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Stx.Core.Queue;
using Stx.Data;
using Stx.Tools;
using Stx.ViewModels;

namespace Stx.App;

/// <summary>Builds the working app: database, tools, queue engine and view models, joined up once.</summary>
public sealed class Composition : IDisposable
{
    static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    readonly SqliteConnection _db;
    readonly HttpClient _http;
    readonly DispatcherQueue _ui;
    readonly DispatcherQueueTimer _retryTimer;
    readonly CancellationTokenSource _closing = new();
    readonly Dictionary<long, long> _lastProgress = new();

    public Composition(DispatcherQueue ui)
    {
        _ui = ui;
        _db = Db.Open(Db.DefaultPath());
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("STXDownloader");

        CredentialFile.DeleteLeftovers();
        var process = new ProcessRunner();
        var catalog = new MediaRepo(_db);
        var vault = new CaptureVault();
        Tools = new ToolManager(_http, ToolManager.DefaultRoot(), new ToolOptions(UpdatesEnabled: true), process);

        var runner = new YtDlpRunner(process, Tools, job => RequestFor(job, catalog), vault);
        Engine = new QueueEngine(new JobRepo(_db), runner, new QueuePolicy(), new SystemClock(), vault);
        Queue = new QueueViewModel(Engine, catalog, new SystemShell(), OnUi);
        Source = new SourceViewModel(new YtDlpLinkAnalyzer(process, () => Tools.YtDlpPath, () => Tools.Paths), catalog, Engine);

        runner.LogLine += (_, line) => OnUi(() => Queue.AddLog(line.JobId, line.Text));
        runner.FileSaved += (_, file) => OnUi(() => Queue.RecordFile(file.JobId, file.Path));
        runner.ProgressChanged += (_, update) => ShowProgress(update);

        // The timer exists for retries, so it runs only while one is waiting. An idle app does no work.
        _retryTimer = ui.CreateTimer();
        _retryTimer.Interval = TimeSpan.FromSeconds(1);
        _retryTimer.Tick += (_, _) => { Engine.Tick(); UpdateRetryTimer(); };
        Engine.JobChanged += (_, _) => OnUi(UpdateRetryTimer);
    }

    public ToolManager Tools { get; }
    public QueueEngine Engine { get; }
    public QueueViewModel Queue { get; }
    public SourceViewModel Source { get; }
    public EngineStatusViewModel Status { get; } = new();

    /// <summary>Call once the window is up, because a first run downloads tools and that must not hold up the app opening.</summary>
    public async Task StartAsync()
    {
        Queue.Load();
        Engine.RecoverAfterRestart();
        var progress = new Progress<ToolStatus>(Status.Report);
        try
        {
            var health = await Tools.EnsureAsync(progress, _closing.Token);
            Status.Apply(health);
            Engine.Tick();
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        _retryTimer.Stop();
        _http.Dispose();
        _db.Dispose();
        _closing.Dispose();
    }

    DownloadRequest RequestFor(Job job, IMediaCatalog catalog) =>
        new(job.Kind == JobKind.Link ? catalog.GetItem(job.MediaId)?.PageUrl : null, OutputFolder(), DownloadMode.Best, null) { Tools = Tools.Paths };

    static string OutputFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "STX Downloader");

    void UpdateRetryTimer()
    {
        if (Queue.HasWaitingJobs) { if (!_retryTimer.IsRunning) _retryTimer.Start(); }
        else _retryTimer.Stop();
    }

    // A download reports many times a second. Each shown update redraws glowing text, so the rate is capped.
    void ShowProgress(JobProgress update)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_lastProgress)
        {
            if (_lastProgress.TryGetValue(update.JobId, out long last) && Stopwatch.GetElapsedTime(last, now) < ProgressInterval) return;
            _lastProgress[update.JobId] = now;
        }
        OnUi(() => Queue.SetProgress(update.JobId, update.Progress));
    }

    void OnUi(Action action)
    {
        if (_ui.HasThreadAccess) action();
        else _ui.TryEnqueue(() => action());
    }

    sealed class SystemShell : IShell
    {
        public void OpenPage(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
