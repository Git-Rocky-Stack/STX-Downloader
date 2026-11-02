using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Stx.App.Primitives;
using Stx.Audio;
using Stx.Bridge;
using Stx.App.Platform;
using Stx.Core.Files;
using Stx.Core.Queue;
using Stx.Core.Settings;
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
    readonly MediaRepo _catalog;
    readonly SemaphoreSlim _measuring = new(1, 1);
    readonly IdenticalFileCheck _identical;
    PipeServer? _bridge;
    LibraryRowViewModel? _selectedRow;

    public Composition(DispatcherQueue ui)
    {
        _ui = ui;
        Settings = new SettingsViewModel(new SettingsStore(SettingsStore.DefaultPath()), new StartupRegistry());
        LookState.Mode = LookStartup.Choose(Settings.Current, windowsIsDark: true);
        LookState.ReduceMotionChosen = Settings.Current.ReduceMotion;
        _db = Db.Open(Db.DefaultPath());
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("STXDownloader");

        CredentialFile.DeleteLeftovers();
        var process = new ProcessRunner();
        var catalog = new MediaRepo(_db);
        _catalog = catalog;
        _identical = new IdenticalFileCheck(catalog.ExistingFacts, catalog.SetFileIdentity, File.Exists, File.Delete);
        var vault = new CaptureVault();
        Tools = new ToolManager(_http, ToolManager.DefaultRoot(), new ToolOptions(Settings.Current.ToolUpdates), process);

        var runner = new YtDlpRunner(process, Tools, job => RequestFor(job, catalog), vault);
        var probe = new Lazy<EncoderProbe>(() => new EncoderProbe(process, Tools.Paths.Ffmpeg ?? ""));
        var converter = new ConversionRunner(
            new FfmpegRunner(process, Path.Combine(ToolManager.DefaultRoot(), "ffmpeg.exe"), () => probe.Value.BestH264Async()),
            catalog.FilePath,
            (media, path, format) => catalog.AddFile(media, path, null, format));
        Engine = new QueueEngine(new JobRepo(_db), new OffThreadRunner(new JobRouter(runner, converter)), new QueuePolicy { MaxParallel = Settings.Current.MaxParallel }, new SystemClock(), vault);
        var shell = new SystemShell();
        Queue = new QueueViewModel(Engine, catalog, shell, OnUi);
        SourceTruth = new SourceTruthViewModel(() => LookState.ReduceMotion);
        Source = new SourceViewModel(new YtDlpLinkAnalyzer(process, () => Tools.YtDlpPath, () => Tools.Paths), catalog, Engine, () => SourceTruth.SelectedOutput);
        Library = new LibraryViewModel(catalog, Engine, shell, File.Exists);

        FirstRun = new FirstRunViewModel(Tools, Status, () => Settings.Current.ToolUpdates);
        Clipboard = new ClipboardOfferViewModel(() => Settings.Current.ClipboardDetect);
        Source.ToolsReady = false;
        FirstRun.PropertyChanged += (_, _) => Source.ToolsReady = FirstRun.ToolsUsable(ToolsOnDisk());
        Settings.Changed += (_, settings) => Apply(settings);

        Queue.FileRecorded += (_, media) => _ = SettleAsync(media);
        converter.FileSaved += (_, media) => OnUi(() => _ = SettleAsync(media));
        converter.LogLine += (_, line) => OnUi(() => Queue.AddLog(line.JobId, line.Text));
        converter.ProgressChanged += (_, update) => ShowProgress(update);
        Source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SourceViewModel.ReportedAudio) && Source.ReportedAudio is { } reported) SourceTruth.Show("", reported);
        };

        runner.LogLine += (_, line) => OnUi(() => Queue.AddLog(line.JobId, line.Text));
        runner.FileSaved += (_, file) => OnUi(() => Queue.RecordFile(file.JobId, file.Path, file.Reused));
        runner.RolledBack += (_, _) => OnUi(() => Status.Report(new ToolStatus("yt-dlp", "rolled back after a failed update")));
        runner.ProgressChanged += (_, update) => ShowProgress(update);

        // The timer exists for retries, so it runs only while one is waiting. An idle app does no work.
        _retryTimer = ui.CreateTimer();
        _retryTimer.Interval = TimeSpan.FromSeconds(1);
        _retryTimer.Tick += (_, _) => { Engine.Tick(); UpdateRetryTimer(); };
        Engine.JobChanged += (_, _) => OnUi(UpdateRetryTimer);
        // A pending retry is real work, so it carries on while the window is hidden. A timer already running is left be.
        Activity.Register(() => Queue.HasWaitingJobs, () => { if (!_retryTimer.IsRunning) _retryTimer.Start(); }, () => _retryTimer.Stop(), keepRunningWhenHidden: true);
    }

    public ToolManager Tools { get; }
    public QueueEngine Engine { get; }
    public QueueViewModel Queue { get; }
    public SourceViewModel Source { get; }
    public SourceTruthViewModel SourceTruth { get; }
    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }
    public FirstRunViewModel FirstRun { get; }
    public ClipboardOfferViewModel Clipboard { get; }
    public WindowActivity Activity { get; } = new();
    public EngineStatusViewModel Status { get; } = new();

    /// <summary>Call once the window is up, because a first run downloads tools and that must not hold up the app opening.</summary>
    public async Task StartAsync()
    {
        Queue.Load();
        Library.Refresh();
        Engine.RecoverAfterRestart();
        try
        {
            // Tools already on disk can work at once, even while their update check is still going.
            Source.ToolsReady = FirstRun.ToolsUsable(ToolsOnDisk());
            StartBridge();
            await FirstRun.StartAsync(_closing.Token);
            Engine.Tick();
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
    }

    bool ToolsOnDisk() => File.Exists(Tools.YtDlpPath) && Tools.Paths.Ffmpeg is not null;

    // Listens for clicks in the browser extension. It's a named pipe only your account can open, so no network port is involved.
    void StartBridge()
    {
        var handler = new CaptureHandler(_catalog, Engine, () => Source.ToolsReady);
        _bridge = new PipeServer(PipeServer.NameForCurrentUser(), handler.Handle);
        try
        {
            _bridge.Start();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another copy of the pipe is open, so captures can't arrive here. Everything else still works.
            _bridge = null;
            Status.Report(new ToolStatus("browser extension", "can't connect to this copy of the app"));
        }
    }

    /// <summary>A file was chosen in the library: show what's known, measuring it first if it never has been.</summary>
    public void Select(LibraryRowViewModel? row)
    {
        _selectedRow = row;
        if (row is null) { SourceTruth.Clear(); return; }

        string? stored = _catalog.LoadFacts(row.FileId);
        if (FactsJson.IsNoAudio(stored)) SourceTruth.ShowNoAudio(row.Title);
        else if (FactsJson.Read(stored) is { } facts) SourceTruth.Show(row.Title, facts);
        else if (!row.IsMissing) _ = MeasureAsync(new SavedMedia(row.MediaId, row.FileId, row.Path, row.Title));
        else SourceTruth.Clear();
    }

    /// <summary>Converts the selected file to the output chosen on the rotary.</summary>
    public void ConvertSelected()
    {
        if (_selectedRow is { } row) Library.ConvertCommand.Execute(new ConvertAsk(row, SourceTruth.SelectedOutput));
    }

    // A newly saved file: find out if its bytes are already here, then measure whichever file is kept.
    async Task SettleAsync(SavedMedia media)
    {
        Library.Refresh();
        SavedMedia settled;
        try
        {
            await _measuring.WaitAsync(_closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            settled = await _identical.SettleAsync(media, _closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _measuring.Release();
        }

        if (settled.Path != media.Path && media.JobId is { } job) OnUi(() => Queue.MarkAlreadySaved(job, settled.Path));
        await MeasureAsync(settled).ConfigureAwait(false);
    }

    // One at a time and below normal priority, so measuring a batch never competes with what you're doing.
    async Task MeasureAsync(SavedMedia media)
    {
        OnUi(Library.Refresh);
        if (Tools.Paths.Ffmpeg is not { } ffmpeg || Tools.FfprobePath is not { } ffprobe) return;

        await _measuring.WaitAsync(_closing.Token).ConfigureAwait(false);
        try
        {
            var facts = await new AudioMeasurer(ffmpeg, ffprobe).MeasureAsync(media.Path, _closing.Token).ConfigureAwait(false);
            _catalog.SaveFacts(media.FileId, facts is null ? FactsJson.NoAudio : FactsJson.Write(facts));
            OnUi(() =>
            {
                if (facts is null) SourceTruth.ShowNoAudio(media.Title);
                else SourceTruth.ShowFinishedAnalysis(media.Title, facts);
            });
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        finally
        {
            _measuring.Release();
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        _bridge?.Dispose();
        // Tools end and their credential files go now, not at the next launch. Dispose isn't on a thread the jobs need.
        Engine.StopAllAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        _retryTimer.Stop();
        _http.Dispose();
        _db.Dispose();
        _measuring.Dispose();
        _closing.Dispose();
    }

    DownloadRequest RequestFor(Job job, IMediaCatalog catalog)
    {
        var item = catalog.GetItem(job.MediaId);
        var (mode, height) = DownloadChoices.Map(Settings.Current.Download);
        string Named(string template) => job.Kind == JobKind.Captured ? FilenameTemplate.ForCaptured(template, item?.Title, job.MediaId) : template;
        string chosen = Settings.Current.FilenameTemplate;
        return new(job.Kind == JobKind.Link ? item?.PageUrl : null, Settings.Current.OutputFolder, mode, height)
        {
            Tools = Tools.Paths,
            Metadata = DownloadChoices.Metadata(Settings.Current),
            OutputTemplate = Named(chosen),
            UniqueTemplate = FilenameTemplate.UniqueVariant(chosen) is { } unique ? Named(unique) : null,
            IsOwnFile = path => catalog.ExistingFacts().Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)
                && (f.MediaId == job.MediaId || (item?.SourceId is { } source && f.SourceId == source))),
        };
    }

    void Apply(AppSettings settings)
    {
        LookState.Mode = settings.LookMode;
        LookState.ReduceMotionChosen = settings.ReduceMotion;
        Tools.Options = new ToolOptions(settings.ToolUpdates);
        Engine.Policy = new QueuePolicy { MaxParallel = settings.MaxParallel };
        Engine.Tick();
    }

    void UpdateRetryTimer()
    {
        Activity.Refresh();
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

        public void OpenFile(string path)
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public void RevealFile(string path)
        {
            if (!File.Exists(path)) return;
            var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            explorer.ArgumentList.Add($"/select,{path}");
            Process.Start(explorer);
        }
    }
}
