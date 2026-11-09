using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Panels;
using Stx.Audio;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Core.Settings;
using Stx.ViewModels;

namespace Stx.App.Gallery;

/// <summary>The real panels on sample jobs, for capture. Nothing here reaches the tools or the database.</summary>
internal static class ConsoleSample
{
    public static UIElement Build()
    {
        var jobs = new Memory();
        var catalog = new Catalog();
        var engine = new QueueEngine(jobs, new NeverRuns(), new QueuePolicy { MaxParallel = 0 }, new SystemClock(), new CaptureVault());
        var queue = new QueueViewModel(engine, catalog, new NoShell(), run => run());

        Seed("SAMPLE MUSIC VIDEO", JobState.Running, null);
        Seed("SAMPLE TECHNICAL WEBINAR", JobState.Completed, null);
        Seed("SAMPLE EXPIRED CAPTURE", JobState.NeedsRecapture, ErrorKind.LinkExpired);
        queue.Load();
        void Seed(string title, JobState state, ErrorKind? error)
        {
            long media = catalog.AddItem("https://example.com/sample", title, title);
            jobs.Add(new Job(0, media, JobKind.Link, state, 0, error, "Keep original", DateTimeOffset.UnixEpoch));
        }
        queue.SetProgress(1, new Stx.Tools.Progress(62, "412MiB", "6.2MiB/s", "00:41"));

        var status = new EngineStatusViewModel();
        status.Apply(Stx.Tools.ToolHealth.Current);
        var source = new SourceViewModel(new NoAnalyzer(), catalog, engine);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(status));
        rack.Children.Add(new SourcePanel(source));
        rack.Children.Add(new QueuePanel(queue));
        // Starts scrolled, so the queue's pads are whole in the 601 pixel window these pictures are taken at.
        var scroller = new ScrollViewer { Content = rack };
        scroller.Loaded += (_, _) => scroller.ChangeView(null, 44, null, disableAnimation: true);
        return scroller;
    }

    /// <summary>The source plate after a sample playlist link was analyzed, so the tutorial shows the real offer. Nothing is fetched.</summary>
    public static UIElement Playlist()
    {
        var catalog = new Catalog();
        var engine = new QueueEngine(new Memory(), new NeverRuns(), new QueuePolicy { MaxParallel = 0 }, new SystemClock(), new CaptureVault());
        var source = new SourceViewModel(new SamplePlaylist(), catalog, engine) { Link = "https://www.youtube.com/watch?v=SAMPLE00001&list=PLSAMPLE" };
        _ = source.AnalyzeCommand.ExecuteAsync(null);
        return new ScrollViewer { Content = new SourcePanel(source) { Margin = new Thickness(8) } };
    }

    /// <summary>The panel on a sample measurement. The product only ever shows values measured from a real file.</summary>
    public static UIElement SourceTruth()
    {
        var bands = Enumerable.Range(0, 96).Select(i => 20 * Math.Pow(1200, i / 95.0)).Select((hz, i) => new SpectrumBand(hz, hz < 17800 ? -18.0 - i * 0.45 : -115.0)).ToList();
        var facts = new AudioFacts("aac", 386, 48000, 6, 17800, -14.2, -1.5, -3.4, -5.1, bands, [], MeasuredFromFile: true);
        var truth = new SourceTruthViewModel(() => true);
        truth.Show("Sample", facts);
        truth.SelectedOutputIndex = 1;
        return new SourceTruthPanel(truth);
    }

    public static UIElement LibraryPage()
    {
        var catalog = new Catalog();
        var jobs = new QueueEngine(new Memory(), new NeverRuns(), new QueuePolicy { MaxParallel = 0 }, new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(new SampleLibrary(), jobs, new NoShell(), path => !path.Contains("moved"), _ => { });
        library.Refresh();
        return new LibraryPanel(library, _ => { });
    }

    public static UIElement Settings()
    {
        string file = Path.Combine(Path.GetTempPath(), "stx-gallery-settings-" + Environment.ProcessId + ".json");
        // A fixed sample, so a baseline never holds a real user's folder. Without WatchedFolders the panel would show
        // this machine's own Videos and Music.
        File.WriteAllText(file, "{\"OutputFolder\":\"D:\\\\Media\\\\STX Downloader\",\"WatchedFolders\":[\"D:\\\\Media\\\\Videos\",\"D:\\\\Media\\\\Music\"]}");
        return new SettingsPanel(new SettingsViewModel(new SettingsStore(file), new NoStartup()), null, () => { }, () => { });
    }

    /// <summary>The top of the real rack with settings open, and the plates its help pads open, so the whole stack can be captured.</summary>
    public static UIElement Rack()
    {
        var status = new EngineStatusViewModel();
        status.Apply(Stx.Tools.ToolHealth.Current);
        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(status, () => { }));
        rack.Children.Add(Settings());
        return new ScrollViewer { Content = rack };
    }

    public static UIElement Guide() => new ScrollViewer { Content = new GuidePanel(() => { }) { Margin = new Thickness(8) } };

    /// <summary>Opened straight away, so a capture shows the plate as it is when the pad is pressed.</summary>
    public static UIElement Tutorial()
    {
        var plate = new TutorialPanel(() => { }) { Margin = new Thickness(8) };
        plate.Loaded += (_, _) => plate.Open();
        plate.Unloaded += (_, _) => plate.Stop();
        return new ScrollViewer { Content = plate };
    }

    public static UIElement FirstRun()
    {
        var first = new FirstRunViewModel(new OfflineSetup(), new EngineStatusViewModel());
        _ = first.StartAsync();
        return new FirstRunPanel(first);
    }

    internal sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled => false;
        public void Set(bool enabled) { }
    }

    sealed class OfflineSetup : Stx.Tools.IToolSetup
    {
        public Task<Stx.Tools.ToolHealth> EnsureAsync(IProgress<Stx.Tools.ToolStatus>? progress = null, CancellationToken cancel = default) =>
            Task.FromResult(Stx.Tools.ToolHealth.Missing);
    }

    sealed class SampleLibrary : InMemoryLibrary
    {
        protected override IReadOnlyList<LibraryItem> AllItems() =>
        [
            new(1, "Sample music video", "https://example.com/1", [new LibraryFile(1, @"D:\Media\Sample.mp4", "Original"), new LibraryFile(2, @"D:\Media\Sample.flac", "Flac")]),
            new(2, "Sample moved file", "https://example.com/2", [new LibraryFile(3, @"D:\moved\Webinar.mkv", "Original")]),
        ];
    }

    sealed class Memory : IJobRepo
    {
        readonly Dictionary<long, Job> _jobs = new();
        long _next = 1;
        public Job Add(Job job) { var saved = job with { Id = _next++ }; _jobs[saved.Id] = saved; return saved; }
        public void Update(Job job) => _jobs[job.Id] = job;
        public Job? Get(long id) => _jobs.GetValueOrDefault(id);
        public IReadOnlyList<Job> All() => _jobs.Values.OrderBy(j => j.Id).ToList();
    }

    sealed class Catalog : IMediaCatalog
    {
        readonly List<MediaRecord> _items = new();
        public long AddItem(string? pageUrl, string? sourceId, string? title) { _items.Add(new MediaRecord(_items.Count + 1, pageUrl, sourceId, title)); return _items.Count; }
        public MediaRecord? GetItem(long id) => _items.FirstOrDefault(i => i.Id == id);
        public long AddFile(long mediaId, string path, string? hash, string format) => 0;
        public IReadOnlyList<MediaFact> ExistingFacts() => [];
        public void RememberCapture(string key, long mediaId) { }
        public long? RememberedCapture(string key) => null;
    }

    sealed class NeverRuns : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) => new TaskCompletionSource<JobOutcome>().Task;
    }

    sealed class NoShell : IShell
    {
        public void OpenPage(string url) { }
        public void OpenFile(string path) { }
        public void RevealFile(string path) { }
    }

    sealed class SamplePlaylist : ILinkAnalyzer
    {
        public Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default) => Task.FromResult(LinkAnalysis.Found("Sample playlist",
        [
            new Stx.Tools.PlaylistEntry("SAMPLE00001", "Sample music video", "https://www.youtube.com/watch?v=SAMPLE00001", true),
            new Stx.Tools.PlaylistEntry("SAMPLE00002", "Sample live session", "https://www.youtube.com/watch?v=SAMPLE00002", true),
            new Stx.Tools.PlaylistEntry("SAMPLE00003", "Sample interview", "https://www.youtube.com/watch?v=SAMPLE00003", true),
        ]));
    }

    sealed class NoAnalyzer : ILinkAnalyzer
    {
        public Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default) => Task.FromResult(LinkAnalysis.Failed(ErrorKind.ToolFailure));
    }
}
