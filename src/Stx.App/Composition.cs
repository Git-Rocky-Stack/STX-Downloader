using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Dispatching;
using Stx.App.Primitives;
using Stx.Audio;
using Stx.Bridge;
using Stx.App.Platform;
using Stx.Core.Files;
using Stx.Core.Library;
using Stx.Core.Logging;
using Stx.Core.Playback;
using Stx.Playback;
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
    // The updater's own: it follows redirects itself, to keep each one to the tool hosts.
    readonly HttpClient _toolsHttp = ToolManager.NewHttpClient();
    readonly DispatcherQueue _ui;
    readonly DispatcherQueueTimer _retryTimer;
    readonly CancellationTokenSource _closing = new();
    readonly Dictionary<long, long> _lastProgress = new();
    readonly MediaRepo _catalog;
    readonly SemaphoreSlim _measuring = new(1, 1);
    readonly IdenticalFileCheck _identical;
    readonly CollectionWatcher _watcher;
    // A scan or a job leaves garbage an idle app never collects; it's given back once the work has been quiet.
    readonly Stx.Core.Memory.MemoryReturn _memory = new(TimeSpan.FromSeconds(10));
    readonly StreamOpener _streams;
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
        Tools = new ToolManager(_toolsHttp, ToolManager.DefaultRoot(), new ToolOptions(Settings.Current.ToolUpdates), process);

        var runner = new YtDlpRunner(process, Tools, job => RequestFor(job, catalog), vault);
        var probe = new Lazy<EncoderProbe>(() => new EncoderProbe(process, () => Tools.Paths.Ffmpeg));
        var converter = new ConversionRunner(
            new FfmpegRunner(process, Path.Combine(ToolManager.DefaultRoot(), "ffmpeg.exe"), () => probe.Value.BestH264Async(),
                new CopyCheck(new ProcessRunner(ProcessPriorityClass.BelowNormal), Path.Combine(ToolManager.DefaultRoot(), "ffmpeg.exe"), Path.Combine(ToolManager.DefaultRoot(), "ffprobe.exe")),
                encoder => probe.Value.HasAsync(encoder)),
            catalog.FilePath,
            (media, path, format) => catalog.AddFile(media, path, null, format));
        Engine = new QueueEngine(new JobRepo(_db), new OffThreadRunner(new JobRouter(runner, converter)), new QueuePolicy { MaxParallel = Settings.Current.MaxParallel }, new SystemClock(), vault);
        var shell = new SystemShell();
        Queue = new QueueViewModel(Engine, catalog, shell, OnUi);
        SourceTruth = new SourceTruthViewModel(() => LookState.ReduceMotion);
        Source = new SourceViewModel(new YtDlpLinkAnalyzer(process, () => Tools.YtDlpPath, () => Tools.Paths), catalog, Engine, () => SourceTruth.SelectedOutput);
        // The Library holds only the rows read so far; its list asks for more as it scrolls (IncrementalRows).
        Library = new LibraryViewModel(catalog, Engine, shell, File.Exists, Play, rows: new Panels.IncrementalRows());
        // BIT-PERFECT's film route is fixed when LibVLC starts; its music route follows the setting from the next file.
        PlayerEngine = new LibVlcEngine(PerformanceProfile.For(Settings.Current.Performance, HardwareProbe.Detect())) { Passthrough = Settings.Current.BitPerfect };
        Player = new PlayerViewModel(new RoutingEngine(PlayerEngine, () => Settings.Current.BitPerfect, () => Tools.Paths.Ffmpeg, () => Tools.FfprobePath), OnUi);
        Opened = new OpenedFiles(catalog, Library);
        // ENHANCE: set on the engine before its swap chain arrives, so the first LibVLC already has it.
        Enhance = new EnhanceViewModel(Settings, () => PerformanceProfile.For(Settings.Current.Performance, HardwareProbe.Detect()), HardwareProbe.Gpu(),
            level => PlayerEngine.Enhance = level, () => PlayerEngine.EnhanceSeen);
        Player.PropertyChanged += (_, _) => Enhance.Refresh();
        _streams = new StreamOpener(_http);
        Shrink = new ShrinkFolder(catalog, Library, path => Path.GetFileName(path).StartsWith(FfmpegRunner.PartialPrefix, StringComparison.Ordinal));
        // The watched folders. The downloads folder is left to the queue, which records each download itself.
        // Copies being written (shrink, convert) carry a temporary name and are recorded by the queue once they're done.
        _watcher = new CollectionWatcher(catalog, path => FolderScan.IsUnder(path, Settings.Current.OutputFolder)
            || Path.GetFileName(path).StartsWith(FfmpegRunner.PartialPrefix, StringComparison.Ordinal));
        _watcher.Changed += (_, _) => OnUi(() => _ = ErrorLog.Observe(Library.RefreshAsync(), "library"));
        _watcher.Progress += (_, progress) =>
        {
            OnUi(() => Library.ShowScan(progress));
            if (progress.Done) _memory.WorkEnded();
        };

        DefaultPlayer = new DefaultPlayerViewModel(new UserRegistry(), link => Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }),
            Settings, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "STXDownloader.exe"), Environment.OSVersion.Version.Build, UserRegistry.AssociationsChanged);
        FirstRun = new FirstRunViewModel(Tools, Status, () => Settings.Current.ToolUpdates);
        Clipboard = new ClipboardOfferViewModel(() => Settings.Current.ClipboardDetect);
        Source.ToolsReady = false;
        FirstRun.PropertyChanged += (_, _) => Source.ToolsReady = FirstRun.ToolsUsable(ToolsOnDisk());
        Settings.Changed += (_, settings) => Apply(settings);

        Queue.FileRecorded += (_, media) => _ = ErrorLog.Observe(SettleAsync(media), "saved file");
        converter.FileSaved += (_, media) => OnUi(() => _ = ErrorLog.Observe(SettleAsync(media), "saved file"));
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
        Engine.JobChanged += (_, job) =>
        {
            OnUi(UpdateRetryTimer);
            if (job.State is Stx.Core.Jobs.JobState.Completed or Stx.Core.Jobs.JobState.Failed or Stx.Core.Jobs.JobState.Canceled) _memory.WorkEnded();
        };
        // A pending retry is real work, so it carries on while the window is hidden. A timer already running is left be.
        Activity.Register(() => Queue.HasWaitingJobs, () => { if (!_retryTimer.IsRunning) _retryTimer.Start(); }, () => _retryTimer.Stop(), keepRunningWhenHidden: true);
    }

    public ToolManager Tools { get; }
    public QueueEngine Engine { get; }
    public QueueViewModel Queue { get; }
    public SourceViewModel Source { get; }
    public SourceTruthViewModel SourceTruth { get; }
    public LibraryViewModel Library { get; }
    public PlayerViewModel Player { get; }
    /// <summary>The engine the PLAYER plate draws from. Its profile follows the PERFORMANCE setting from the next file on.</summary>
    public LibVlcEngine PlayerEngine { get; }
    public OpenedFiles Opened { get; }
    public EnhanceViewModel Enhance { get; }
    /// <summary>SHRINK FOLDER.</summary>
    public ShrinkFolder Shrink { get; }
    public SettingsViewModel Settings { get; }
    public FirstRunViewModel FirstRun { get; }
    /// <summary>STX as the default player: asked once at first run, and a pad and LED in Settings.</summary>
    public DefaultPlayerViewModel DefaultPlayer { get; }
    public ClipboardOfferViewModel Clipboard { get; }
    public WindowActivity Activity { get; } = new();
    public EngineStatusViewModel Status { get; } = new();

    /// <summary>Call once the window is up, because a first run downloads tools and that must not hold up the app opening.</summary>
    public async Task StartAsync()
    {
        // Review 2026-11-07: the loads ran outside the try, so a locked database or a damaged row faulted this task
        // with nothing to see it, and the bridge, the tool check and the queue never started: a console that looked
        // dead. Each step now stands on its own, says what failed, and the next one still runs.
        Step("queue", Queue.Load);
        try { await Library.RefreshAsync(); }
        catch (Exception e) when (e is not OperationCanceledException) { Failed("library", e); }
        // The first scan runs in the background below normal priority; rows appear as it goes.
        Step("library watcher", () => _watcher.Watch(Settings.Current.Watched));
        Step("queue", Engine.RecoverAfterRestart);
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
        catch (Exception e)
        {
            Failed("tools", e);
        }
    }

    void Step(string what, Action step)
    {
        try { step(); }
        catch (Exception e) when (e is not OperationCanceledException) { Failed(what, e); }
    }

    // Written to the error log, and said on the status line, so a failure at startup is never silent.
    void Failed(string what, Exception e)
    {
        ErrorLog.Write("startup: " + what, e);
        OnUi(() => Status.Report(new ToolStatus(what, "couldn't start; details are in the error log")));
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
        else if (!row.IsMissing) _ = ErrorLog.Observe(MeasureAsync(new SavedMedia(row.MediaId, row.FileId, row.Path, row.Title)), "measuring");
        else SourceTruth.Clear();
        // Watched and opened files are indexed lightly. Their bytes are hashed the first time they're chosen, so a
        // later download of the same file is recognised.
        if (row.Origin != MediaOrigin.Downloaded && !row.IsMissing) _ = ErrorLog.Observe(HashOnFirstUseAsync(row), "hashing");
    }

    async Task HashOnFirstUseAsync(LibraryRowViewModel row)
    {
        if (_catalog.FileHash(row.FileId) is not null) return;
        try
        {
            string hash = await Task.Run(() => IdenticalFileCheck.HashOfAsync(row.Path, _closing.Token)).ConfigureAwait(false);
            _catalog.SetFileIdentity(row.FileId, hash, row.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Unreadable or closing: it's hashed the next time it's chosen.
        }
    }

    /// <summary>Plays a Library row on the PLAYER plate. PREV and NEXT walk the Library in its order, as its search narrows it, read from the database at each step.</summary>
    public void Play(LibraryRowViewModel row)
    {
        Select(row);
        Player.Play(row.Path, row.Title, Library.PlayOrder());
    }

    /// <summary>
    /// PLAY STREAM: a stream address or a radio station plays on the PLAYER plate, without saving anything. A web page
    /// is sent to ANALYZE, and an address that would need a listening port is refused, both with a plain line.
    /// </summary>
    public async Task PlayStreamAsync(string text)
    {
        using var patience = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        patience.CancelAfter(TimeSpan.FromSeconds(15));
        StreamChoice choice;
        try { choice = await _streams.ResolveAsync(text, patience.Token); }
        catch (OperationCanceledException) { choice = new(null, "", [], StreamOpener.Unreachable); }
        Source.Report(choice.Message);
        if (!choice.CanPlay) return;
        var list = new List<(string Path, string Title)> { (choice.Url!, choice.Title) };
        list.AddRange(choice.Fallbacks.Select(f => (f, StreamOpener.TitleOf(f))));
        Player.Play(choice.Url!, choice.Title, list);
    }

    /// <summary>PLAY DISC: a DVD or Blu-ray drive, disc folder or image. The engine checks it again and refuses an encrypted one.</summary>
    public void PlayDisc(string path)
    {
        var verdict = DiscCheck.Inspect(path);
        if (verdict.Kind == DiscKind.None)
        {
            Source.Report("THAT ISN'T A DVD OR BLU-RAY. PICK THE DRIVE, OR THE FOLDER THAT HOLDS VIDEO_TS OR BDMV.");
            return;
        }
        Source.Report(verdict.Encrypted ? DiscCheck.Refused : "");
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        string title = $"{(verdict.Kind == DiscKind.BluRay ? "BLU-RAY" : "DVD")} · {(name.Length > 0 ? name : path)}";
        Player.Play(path, title, [(path, title)]);
    }

    /// <summary>A file opened from outside STX joins the Library and plays. Call it from the UI thread.</summary>
    public async Task OpenFromOutsideAsync(string path)
    {
        LibraryRowViewModel row;
        try { row = await Opened.AdoptAsync(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
        {
            ErrorLog.Write("open from outside", e);
            Source.Report("THAT FILE COULDN'T BE ADDED TO THE LIBRARY. DETAILS ARE IN THE ERROR LOG.");
            return;
        }
        Play(row);
    }

    /// <summary>Converts the selected file to the output chosen on the rotary.</summary>
    public void ConvertSelected()
    {
        if (_selectedRow is { } row) Library.ConvertCommand.Execute(new ConvertAsk(row, SourceTruth.SelectedOutput));
    }

    // A newly saved file: find out if its bytes are already here, then measure whichever file is kept.
    async Task SettleAsync(SavedMedia media)
    {
        await Library.RefreshAsync();
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
        OnUi(() => _ = ErrorLog.Observe(Library.RefreshAsync(), "library"));
        if (Tools.Paths.Ffmpeg is not { } ffmpeg || Tools.FfprobePath is not { } ffprobe) return;

        await _measuring.WaitAsync(_closing.Token).ConfigureAwait(false);
        try
        {
            var facts = await new AudioMeasurer(ffmpeg, ffprobe).MeasureAsync(media.Path, _closing.Token).ConfigureAwait(false);
            _catalog.SaveFacts(media.FileId, facts is null ? FactsJson.NoAudio : FactsJson.Write(facts));
            OnUi(() =>
            {
                // Review 2026-11-07: a measurement that finished after another row was chosen replaced that row's
                // plate. It's stored either way; it's shown only if its file is still the one chosen, or none is.
                if (_selectedRow is { } chosen && chosen.FileId != media.FileId) return;
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
        _memory.Dispose();
        _bridge?.Dispose();
        _watcher.Dispose();
        // Stops playback and lets the decoder go before anything else closes.
        Player.Dispose();
        // Tools end and their credential files go now, not at the next launch. Dispose isn't on a thread the jobs need.
        Engine.StopAllAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        _retryTimer.Stop();
        _http.Dispose();
        _toolsHttp.Dispose();
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
        _watcher.Watch(settings.Watched);
        Engine.Tick();
        // PERFORMANCE applies from the next file played; nothing is rebuilt and nothing playing is interrupted.
        PlayerEngine.Profile = PerformanceProfile.For(settings.Performance, HardwareProbe.Detect());
        // PERFORMANCE sets ENHANCE's ceiling, so a change there can change what runs.
        Enhance.Reapply();
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
