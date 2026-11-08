using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Look;
using Stx.Tools;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

sealed class MemoryJobs : IJobRepo
{
    readonly Dictionary<long, Job> _jobs = new();
    long _next = 1;
    public Job Add(Job job) { var s = job with { Id = _next++ }; _jobs[s.Id] = s; return s; }
    public void Update(Job job) => _jobs[job.Id] = job;
    public Job? Get(long id) => _jobs.GetValueOrDefault(id);
    public IReadOnlyList<Job> All() => _jobs.Values.OrderBy(j => j.Id).ToList();
}

sealed class MemoryCatalog : IMediaCatalog
{
    public readonly List<MediaRecord> Items = new();
    public readonly List<(long Media, string Path, string Format)> Files = new();

    public long AddItem(string? pageUrl, string? sourceId, string? title)
    {
        long id = Items.Count + 1;
        Items.Add(new MediaRecord(id, pageUrl, sourceId, title));
        return id;
    }

    public MediaRecord? GetItem(long id) => Items.FirstOrDefault(i => i.Id == id);

    public long AddFile(long mediaId, string path, string? hash, string format)
    {
        Files.Add((mediaId, path, format));
        return Files.Count;
    }

    public IReadOnlyList<MediaFact> ExistingFacts() =>
        Files.Select(f => new MediaFact(GetItem(f.Media)?.SourceId, null, f.Format, f.Path)).ToList();

    public void RememberCapture(string key, long mediaId) { }
    public long? RememberedCapture(string key) => null;
}

/// <summary>A job runner the test finishes by hand, on a plain context so results are ready at once.</summary>
sealed class HandRunner : IJobRunner
{
    public readonly Dictionary<long, TaskCompletionSource<JobOutcome>> Runs = new();

    public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
    {
        var run = new TaskCompletionSource<JobOutcome>();
        Runs[job.Id] = run;
        return run.Task;
    }

    public void Finish(long id, JobOutcome outcome)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        Runs[id].SetResult(outcome);
        SynchronizationContext.SetSynchronizationContext(context);
    }
}

sealed class ScriptedAnalyzer(Func<string, LinkAnalysis> script) : ILinkAnalyzer
{
    public readonly List<string> Asked = new();

    public Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default)
    {
        Asked.Add(url);
        return Task.FromResult(script(url));
    }
}

sealed class ShellSpy : IShell
{
    public readonly List<string> Opened = new();
    public readonly List<string> FileActions = new();
    public void OpenPage(string url) => Opened.Add(url);
    public void OpenFile(string path) => FileActions.Add("open:" + path);
    public void RevealFile(string path) => FileActions.Add("reveal:" + path);
}

public class ConsoleFixture
{
    internal readonly MemoryJobs Jobs = new();
    internal readonly MemoryCatalog Catalog = new();
    internal readonly HandRunner Runner = new();
    internal readonly CaptureVault Vault = new();
    internal readonly ShellSpy Shell = new();
    internal readonly QueueEngine Engine;
    internal readonly QueueViewModel Queue;

    public ConsoleFixture()
    {
        Engine = new QueueEngine(Jobs, Runner, new QueuePolicy(), new SystemClock(), Vault);
        Queue = new QueueViewModel(Engine, Catalog, Shell, run => run());
    }

    internal static LinkAnalysis One(string id = "abc", string title = "My Song") =>
        LinkAnalysis.Found(title, [new PlaylistEntry(id, title, "https://x/" + id, true)]);

    internal SourceViewModel Source(ILinkAnalyzer analyzer) => new(analyzer, Catalog, Engine, fileExists: _ => true);

    internal static readonly CaptureSecrets Secret = new("https://cdn/x.m3u8", new Dictionary<string, string>(), null);
}

public class SourcePanelTests : ConsoleFixture
{
    [Fact]
    public async Task Unsupported_link_suggests_browser_capture()
    {
        var source = Source(new ScriptedAnalyzer(_ => LinkAnalysis.Failed(ErrorKind.UnsupportedSite)));
        source.Link = "https://www.chevrolet.com/performance";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Contains("Chrome", source.NextStep);
        Assert.Contains("capture", source.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(source.Message));
        Assert.Empty(Jobs.All());
    }

    [Fact]
    public async Task Sign_in_needed_suggests_opening_in_chrome()
    {
        var source = Source(new ScriptedAnalyzer(_ => LinkAnalysis.Failed(ErrorKind.SignInNeeded)));
        source.Link = "https://x/y";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Contains("Chrome", source.NextStep);
        Assert.Contains("signed in", source.NextStep);
    }

    [Fact]
    public async Task A_single_link_goes_straight_into_the_queue()
    {
        var source = Source(new ScriptedAnalyzer(_ => One()));
        source.Link = "https://www.youtube.com/watch?v=abc";
        await source.AnalyzeCommand.ExecuteAsync(null);

        var job = Assert.Single(Jobs.All());
        Assert.Equal(JobKind.Link, job.Kind);
        Assert.Equal("My Song", Catalog.GetItem(job.MediaId)!.Title);
        Assert.Equal("https://www.youtube.com/watch?v=abc", Catalog.GetItem(job.MediaId)!.PageUrl);
        Assert.Equal("", source.Link);
    }

    // Waits for ever, as yt-dlp does on a host that accepts the connection and then says nothing.
    sealed class StalledAnalyzer : ILinkAnalyzer
    {
        public async Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default)
        {
            await Task.Delay(Timeout.Infinite, cancel);
            throw new InvalidOperationException("unreachable");
        }
    }

    // Review 2026-11-07: ANALYZE on a stalled site stayed busy with no way out.
    [Fact]
    public async Task Analyze_gives_up_on_a_stalled_site_and_says_so()
    {
        var source = new SourceViewModel(new StalledAnalyzer(), Catalog, Engine, fileExists: _ => true, analyzeLimit: TimeSpan.FromMilliseconds(300));
        source.Link = "https://x/never";
        var analyzing = source.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(await Task.WhenAny(analyzing, Task.Delay(TimeSpan.FromSeconds(10))) == analyzing, "ANALYZE never gave up");
        Assert.False(source.IsBusy);
        Assert.Equal(ErrorKind.Network.Message(), source.Message);
    }

    [Fact]
    public async Task Playlist_link_asks_before_enqueuing_all()
    {
        var entries = new[] { new PlaylistEntry("a", "A", "https://x/a", true), new PlaylistEntry("b", "B", "https://x/b", true), new PlaylistEntry("c", "[Private video]", "https://x/c", false) };
        var source = Source(new ScriptedAnalyzer(_ => LinkAnalysis.Found("Mix", entries)));
        source.Link = "https://x/list";

        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Empty(Jobs.All());
        Assert.NotNull(source.PendingPlaylist);
        Assert.Equal("Mix", source.PendingPlaylist!.Title);
        Assert.Equal(2, source.PendingPlaylist.Available);
        Assert.Equal(1, source.PendingPlaylist.Unavailable);

        await source.ConfirmPlaylistCommand.ExecuteAsync(null);
        Assert.Equal(2, Jobs.All().Count);
        Assert.Null(source.PendingPlaylist);
    }

    // Review 2026-11-07: each entry read every saved row again and looked for each saved file on disk, on the UI
    // thread. A 30-entry playlist against 20 saved files was 600 lookups; it's one look at each file now.
    [Fact]
    public async Task Confirming_a_playlist_looks_for_each_saved_file_once()
    {
        for (int i = 0; i < 20; i++) Catalog.AddFile(Catalog.AddItem($"https://x/saved{i}", $"saved{i}", $"Saved {i}"), $@"D:\Saved {i}.mp4", null, SourceViewModel.OriginalFormat);
        int lookups = 0;
        var entries = Enumerable.Range(0, 30).Select(i => new PlaylistEntry($"new{i}", $"New {i}", $"https://x/new{i}", true)).Append(new PlaylistEntry("saved3", "Saved 3", "https://x/saved3", true)).ToArray();
        var source = new SourceViewModel(new ScriptedAnalyzer(_ => LinkAnalysis.Found("Mix", entries)), Catalog, Engine, fileExists: _ => { Interlocked.Increment(ref lookups); return true; });
        source.Link = "https://x/list";
        await source.AnalyzeCommand.ExecuteAsync(null);
        await source.ConfirmPlaylistCommand.ExecuteAsync(null);
        Assert.Equal(20, lookups);
        Assert.Equal(30, Jobs.All().Count);
        Assert.Equal("1 already saved, so skipped.", source.Message);
    }

    [Fact]
    public async Task Declining_a_playlist_enqueues_nothing()
    {
        var entries = new[] { new PlaylistEntry("a", "A", "https://x/a", true), new PlaylistEntry("b", "B", "https://x/b", true) };
        var source = Source(new ScriptedAnalyzer(_ => LinkAnalysis.Found("Mix", entries)));
        source.Link = "https://x/list";
        await source.AnalyzeCommand.ExecuteAsync(null);

        source.DeclinePlaylistCommand.Execute(null);

        Assert.Empty(Jobs.All());
        Assert.Null(source.PendingPlaylist);
    }

    [Fact]
    public async Task Something_already_saved_is_not_downloaded_again()
    {
        long item = Catalog.AddItem("https://x/abc", "abc", "My Song");
        Catalog.AddFile(item, @"D:\m.mp4", null, SourceViewModel.OriginalFormat);
        var source = Source(new ScriptedAnalyzer(_ => One("abc")));
        source.Link = "https://x/abc";

        await source.AnalyzeCommand.ExecuteAsync(null);

        Assert.Empty(Jobs.All());
        Assert.Contains("already", source.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_link_asks_for_one_without_calling_the_tool(string link)
    {
        var analyzer = new ScriptedAnalyzer(_ => One());
        var source = Source(analyzer);
        source.Link = link;
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Empty(analyzer.Asked);
        Assert.False(string.IsNullOrEmpty(source.Message));
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("--exec=calc")]
    public async Task Text_that_is_not_a_web_link_never_reaches_the_tool(string link)
    {
        var analyzer = new ScriptedAnalyzer(_ => One());
        var source = Source(analyzer);
        source.Link = link;
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Empty(analyzer.Asked);
        Assert.Empty(Jobs.All());
    }

    [Fact]
    public async Task Surrounding_spaces_from_a_paste_are_ignored()
    {
        var analyzer = new ScriptedAnalyzer(_ => One());
        var source = Source(analyzer);
        source.Link = "  https://x/abc \n";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal(["https://x/abc"], analyzer.Asked);
    }

    [Fact]
    public async Task A_new_analysis_clears_the_last_error()
    {
        bool fail = true;
        var source = Source(new ScriptedAnalyzer(_ => fail ? LinkAnalysis.Failed(ErrorKind.Network) : One()));
        source.Link = "https://x/1";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(string.IsNullOrEmpty(source.Message));

        fail = false;
        source.Link = "https://x/2";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal("", source.NextStep);
    }

    [Fact]
    public async Task Busy_shows_while_the_tool_reads_the_link()
    {
        var gate = new TaskCompletionSource<LinkAnalysis>();
        var source = new SourceViewModel(new WaitingAnalyzer(gate.Task), Catalog, Engine) { Link = "https://x/1" };
        var run = source.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(source.IsBusy);
        gate.SetResult(One());
        await run;
        Assert.False(source.IsBusy);
    }

    sealed class WaitingAnalyzer(Task<LinkAnalysis> result) : ILinkAnalyzer
    {
        public Task<LinkAnalysis> AnalyzeAsync(string url, CancellationToken cancel = default) => result;
    }
}

public class QueuePanelTests : ConsoleFixture
{
    QueueItemViewModel Item(long id) => Queue.Items.Single(i => i.JobId == id);

    Job Start(JobKind kind = JobKind.Link, CaptureSecrets? secrets = null)
    {
        long media = Catalog.AddItem("https://site/page", "src", "My Song");
        return Engine.Enqueue(media, kind, "Keep original", secrets);
    }

    [Fact]
    public void Starting_job_sets_led_to_Strike_then_Busy()
    {
        var job = Start();
        Assert.Equal(LedState.Strike, Item(job.Id).LedState);
        Assert.Equal(LedState.Busy, StrikeTimeline.After(Item(job.Id).LedState));
    }

    [Fact]
    public void Finished_job_sets_led_On()
    {
        var job = Start();
        Runner.Finish(job.Id, JobOutcome.Success);
        Assert.Equal(LedState.On, Item(job.Id).LedState);
        Assert.Equal(LedColor.Green, Item(job.Id).LedColor);
        Assert.Equal(100, Item(job.Id).Percent);
    }

    [Fact]
    public void Failed_job_lights_red_with_plain_words_and_a_next_step()
    {
        var job = Start();
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.DrmDetected));
        var item = Item(job.Id);
        Assert.Equal(LedColor.Red, item.LedColor);
        Assert.Equal(LedState.On, item.LedState);
        Assert.Equal(ErrorKind.DrmDetected.Message(), item.ErrorMessage);
        Assert.Equal(ErrorKind.DrmDetected.NextStep(), item.NextStep);
    }

    [Fact]
    public void A_queued_job_waits_dark()
    {
        var quiet = new QueueEngine(Jobs, Runner, new QueuePolicy { MaxParallel = 0 }, new SystemClock(), Vault);
        var queue = new QueueViewModel(quiet, Catalog, Shell, run => run());
        var job = quiet.Enqueue(Catalog.AddItem("https://x", "s", "T"), JobKind.Link, "Keep original");
        Assert.Equal(LedState.Off, queue.Items.Single(i => i.JobId == job.Id).LedState);
    }

    [Fact]
    public void Needs_recapture_shows_reopen_action()
    {
        var job = Start(JobKind.Captured, Secret);
        Assert.False(Item(job.Id).ShowsReopen);
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        Assert.True(Item(job.Id).ShowsReopen);
        Assert.Equal(LedColor.Blue, Item(job.Id).LedColor);
    }

    [Fact]
    public void Reopen_opens_the_page_in_the_browser()
    {
        var job = Start(JobKind.Captured, Secret);
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        Queue.Reopen(Item(job.Id));
        Assert.Equal(["https://site/page"], Shell.Opened);
    }

    [Fact]
    public void Link_expired_moves_captured_job_to_recapture()
    {
        var job = Start(JobKind.Captured, Secret);
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        Assert.Equal(JobState.NeedsRecapture, Jobs.Get(job.Id)!.State);
        Assert.Contains("RE-CAPTURE", Item(job.Id).Status);
    }

    [Fact]
    public void Raw_log_is_redacted()
    {
        var job = Start();
        Queue.AddLog(job.Id, "GET https://cdn/x?token=SECRET Cookie: a=SECRET");
        Assert.DoesNotContain("SECRET", Item(job.Id).RawLog);
        Assert.Contains("https://cdn/x", Item(job.Id).RawLog);
    }

    [Fact]
    public void The_log_keeps_the_end_when_it_grows_long()
    {
        var job = Start();
        for (int i = 0; i < 5000; i++) Queue.AddLog(job.Id, $"line {i}");
        Assert.Contains("line 4999", Item(job.Id).RawLog);
        Assert.DoesNotContain("line 0\n", Item(job.Id).RawLog);
    }

    [Fact]
    public void Progress_reaches_the_row()
    {
        var job = Start();
        Queue.SetProgress(job.Id, new Progress(62, "10MiB", "4MiB/s", "00:05"));
        Assert.Equal(62, Item(job.Id).Percent);
        Assert.Contains("4MiB/s", Item(job.Id).Status);
    }

    [Fact]
    public void Progress_for_a_job_that_is_gone_is_ignored() =>
        Queue.SetProgress(404, new Progress(50, null, null, null));

    [Fact]
    public void Progress_arriving_after_a_job_finished_does_not_undo_it()
    {
        var job = Start();
        Runner.Finish(job.Id, JobOutcome.Success);
        Queue.SetProgress(job.Id, new Progress(12, null, "1MiB/s", null));
        Assert.Equal(100, Item(job.Id).Percent);
    }

    [Fact]
    public void Pause_resume_cancel_and_retry_reach_the_engine()
    {
        var job = Start();
        var item = Item(job.Id);

        // A stopped tool reports back as it ends; a job runs again only once its last run has (review 2026-11-07).
        Queue.Pause(item);
        Assert.Equal(JobState.Paused, Jobs.Get(job.Id)!.State);
        Runner.Finish(job.Id, JobOutcome.Failure(Stx.Core.Errors.ErrorKind.ToolFailure));
        Queue.Resume(item);
        Assert.Equal(JobState.Running, Jobs.Get(job.Id)!.State);
        Queue.Cancel(item);
        Assert.Equal(JobState.Canceled, Jobs.Get(job.Id)!.State);
        Runner.Finish(job.Id, JobOutcome.Failure(Stx.Core.Errors.ErrorKind.ToolFailure));
        Queue.Retry(item);
        Assert.Equal(JobState.Running, Jobs.Get(job.Id)!.State);
    }

    [Theory]
    [InlineData(JobState.Running, true, false, true, false)]
    [InlineData(JobState.Paused, false, true, true, false)]
    [InlineData(JobState.Failed, false, false, true, true)]
    [InlineData(JobState.Completed, false, false, false, false)]
    public void Only_actions_that_make_sense_are_offered(JobState state, bool pause, bool resume, bool cancel, bool retry)
    {
        var item = new QueueItemViewModel(1, "T", "https://x");
        item.Apply(new Job(1, 1, JobKind.Link, state, 0, null, "p", DateTimeOffset.UnixEpoch));
        Assert.Equal((pause, resume, cancel, retry), (item.CanPause, item.CanResume, item.CanCancel, item.CanRetry));
    }

    [Fact]
    public void The_queue_comes_back_after_a_restart()
    {
        var job = Start();
        var fresh = new QueueViewModel(Engine, Catalog, Shell, run => run());
        fresh.Load();
        var item = Assert.Single(fresh.Items);
        Assert.Equal(job.Id, item.JobId);
        Assert.Equal("My Song", item.Title);
    }

    [Fact]
    public void Knows_when_something_is_waiting_on_a_retry_so_the_timer_runs_only_then()
    {
        var job = Start();
        Assert.False(Queue.HasWaitingJobs);
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.Network));
        Assert.True(Queue.HasWaitingJobs);
    }

    [Fact]
    public void Saved_files_are_recorded_against_their_media_item()
    {
        var job = Start();
        Queue.RecordFile(job.Id, @"D:\Media\Song.mp4");
        var file = Assert.Single(Catalog.Files);
        Assert.Equal(job.MediaId, file.Media);
        Assert.Equal(SourceViewModel.OriginalFormat, file.Format);
    }

    [Fact]
    public void Work_is_marshaled_onto_the_ui_thread()
    {
        int marshaled = 0;
        var queue = new QueueViewModel(Engine, Catalog, Shell, run => { marshaled++; run(); });
        Start();
        Assert.True(marshaled > 0);
        Assert.NotEmpty(queue.Items);
    }
}

public class EngineStatusTests
{
    [Theory]
    [InlineData(ToolHealth.Current, LedColor.Green, LedState.On, false)]
    [InlineData(ToolHealth.UpdatePending, LedColor.Amber, LedState.On, false)]
    [InlineData(ToolHealth.Degraded, LedColor.Amber, LedState.On, false)]
    [InlineData(ToolHealth.Missing, LedColor.Red, LedState.On, true)]
    public void Engine_led_reflects_tool_health(ToolHealth health, LedColor color, LedState state, bool fault)
    {
        var engine = new EngineStatusViewModel();
        engine.Apply(health);
        Assert.Equal(color, engine.EngineColor);
        Assert.Equal(state, engine.EngineState);
        Assert.Equal(fault ? LedState.On : LedState.Off, engine.FaultState);
        Assert.False(string.IsNullOrWhiteSpace(engine.EngineText));
    }

    [Fact]
    public void Before_the_check_finishes_the_engine_blinks()
    {
        var engine = new EngineStatusViewModel();
        Assert.Equal(LedState.Busy, engine.EngineState);
        Assert.Equal(LedState.Off, engine.FaultState);
    }

    [Fact]
    public void Each_health_says_something_different()
    {
        var texts = Enum.GetValues<ToolHealth>().Select(h => { var e = new EngineStatusViewModel(); e.Apply(h); return e.EngineText; });
        Assert.Equal(4, texts.Distinct().Count());
    }

    [Fact]
    public void Tool_setup_progress_shows_on_the_engine_line()
    {
        var engine = new EngineStatusViewModel();
        engine.Report(new ToolStatus("FFmpeg", "Downloading"));
        Assert.Contains("FFMPEG", engine.EngineText);
    }
}

public class AfterDownloadTests : ConsoleFixture
{
    [Fact]
    public void A_format_chosen_earlier_starts_its_conversion_when_the_download_finishes()
    {
        long media = Catalog.AddItem("https://x", "s", "T");
        var job = Engine.Enqueue(media, JobKind.Link, ConversionPlan.Then(Stx.Core.Audio.OutputChoice.Flac));
        Queue.RecordFile(job.Id, @"D:\m\t.webm");

        Runner.Finish(job.Id, JobOutcome.Success);

        var conversion = Assert.Single(Jobs.All(), j => j.Kind == JobKind.Convert);
        var plan = ConversionPlan.Parse(conversion.Preset)!;
        Assert.Equal(Stx.Core.Audio.OutputChoice.Flac, plan.Output);
        Assert.Equal(media, conversion.MediaId);
    }

    [Fact]
    public void A_failed_download_starts_no_conversion()
    {
        long media = Catalog.AddItem("https://x", "s", "T");
        var job = Engine.Enqueue(media, JobKind.Link, ConversionPlan.Then(Stx.Core.Audio.OutputChoice.Flac));
        Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.DrmDetected));
        Assert.DoesNotContain(Jobs.All(), j => j.Kind == JobKind.Convert);
    }

    [Fact]
    public void A_recorded_file_is_announced_for_measuring()
    {
        SavedMedia? seen = null;
        Queue.FileRecorded += (_, media) => seen = media;
        var job = Engine.Enqueue(Catalog.AddItem("https://x", "s", "My Song"), JobKind.Link, "Keep original");
        Queue.RecordFile(job.Id, @"D:\m\t.mp4");
        Assert.Equal(@"D:\m\t.mp4", seen!.Path);
        Assert.Equal("My Song", seen.Title);
    }
}

public sealed class ConversionRunnerTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-conv").FullName;
    readonly List<(long Media, string Path, string Format)> _added = new();

    public void Dispose() => Directory.Delete(_root, true);

    sealed class Fake(int exit = 0) : IProcessRunner
    {
        public IReadOnlyList<string>? Args;
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            Args = args;
            File.WriteAllText(args[^1], "converted");
            return Task.FromResult(exit);
        }
    }

    ConversionRunner Runner(IProcessRunner process, string? input) => new(
        new FfmpegRunner(process, "ffmpeg.exe"),
        id => input,
        (media, path, format) => { _added.Add((media, path, format)); return 9; });

    static Job Convert(string plan) => new(1, 5, JobKind.Convert, JobState.Running, 0, null, plan, DateTimeOffset.UnixEpoch);

    string Input()
    {
        string path = Path.Combine(_root, "song.webm");
        File.WriteAllText(path, "original");
        return path;
    }

    [Fact]
    public async Task A_conversion_records_a_new_file_and_keeps_the_original()
    {
        string input = Input();
        var outcome = await Runner(new Fake(), input).RunAsync(Convert("convert:Flac:7"), default);
        Assert.True(outcome.IsSuccess);
        var added = Assert.Single(_added);
        Assert.Equal((5L, Path.Combine(_root, "song.flac"), "Flac"), added);
        Assert.Equal("original", File.ReadAllText(input));
    }

    [Fact]
    public async Task A_device_preset_reencodes_at_its_height_and_keeps_its_name_as_the_format()
    {
        var process = new Fake();
        await Runner(process, Input()).RunAsync(Convert("convert:Mp4:7:Phone"), default);
        Assert.Contains(process.Args!, a => a.Contains("720"));
        Assert.Equal("Phone", Assert.Single(_added).Format);
    }

    [Fact]
    public async Task A_file_that_has_gone_is_a_failure_not_a_crash()
    {
        var outcome = await Runner(new Fake(), Path.Combine(_root, "gone.webm")).RunAsync(Convert("convert:Flac:7"), default);
        Assert.False(outcome.IsSuccess);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task An_unknown_file_id_is_a_failure()
    {
        var outcome = await Runner(new Fake(), null).RunAsync(Convert("convert:Flac:7"), default);
        Assert.Equal(ErrorKind.ToolFailure, outcome.Error);
    }

    [Fact]
    public async Task A_job_with_an_unreadable_plan_is_a_failure()
    {
        var outcome = await Runner(new Fake(), Input()).RunAsync(Convert("nonsense"), default);
        Assert.Equal(ErrorKind.ToolFailure, outcome.Error);
    }

    [Fact]
    public async Task Jobs_go_to_the_runner_for_their_kind()
    {
        var seen = new List<string>();
        var router = new JobRouter(new Named("download", seen), new Named("convert", seen));
        await router.RunAsync(Convert("x") with { Kind = JobKind.Link }, default);
        await router.RunAsync(Convert("x") with { Kind = JobKind.Captured }, default);
        await router.RunAsync(Convert("x"), default);
        Assert.Equal(["download", "download", "convert"], seen);
    }

    sealed class Named(string name, List<string> seen) : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
        {
            seen.Add(name);
            return Task.FromResult(JobOutcome.Success);
        }
    }
}

public class FactsJsonTests
{
    [Fact]
    public void Measured_facts_round_trip_including_silence()
    {
        var facts = new Stx.Audio.AudioFacts("aac", 192, 48000, 2, 16000, -14.2, -1.5, double.NegativeInfinity, -12,
            [new Stx.Audio.SpectrumBand(1000, -30)], [new Stx.Audio.EnvelopePoint(double.NegativeInfinity, -6)], true);
        var back = FactsJson.Read(FactsJson.Write(facts))!;
        Assert.Equal(facts.Codec, back.Codec);
        Assert.Equal(double.NegativeInfinity, back.PeakLeftDb);
        Assert.Equal(facts.Spectrum, back.Spectrum);
        Assert.Equal(facts.Envelope, back.Envelope);
        Assert.True(back.MeasuredFromFile);
    }

    [Fact]
    public void A_file_with_no_audio_is_remembered_as_such()
    {
        Assert.True(FactsJson.IsNoAudio(FactsJson.NoAudio));
        Assert.Null(FactsJson.Read(FactsJson.NoAudio));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    public void Damaged_facts_read_as_nothing(string json) => Assert.Null(FactsJson.Read(json));
}
