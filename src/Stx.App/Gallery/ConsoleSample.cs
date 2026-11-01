using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Panels;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
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
        return new ScrollViewer { Content = rack };
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
    }

    sealed class NeverRuns : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) => new TaskCompletionSource<JobOutcome>().Task;
    }

    sealed class NoShell : IShell
    {
        public void OpenPage(string url) { }
    }

    sealed class NoAnalyzer : ILinkAnalyzer
    {
        public Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default) => Task.FromResult(LinkAnalysis.Failed(ErrorKind.ToolFailure));
    }
}
