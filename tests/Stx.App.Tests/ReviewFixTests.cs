using Stx.Core.Audio;
using Stx.Core.Settings;
using Stx.Core.Jobs;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>Reports arrive on a pool thread after the download, and the app's controls only accept the UI thread.</summary>
sealed class OffThreadSetup(ToolHealth answer, Exception? failure = null) : IToolSetup
{
    public async Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default)
    {
        await Task.Delay(1, cancel).ConfigureAwait(false);
        progress?.Report(new ToolStatus("FFmpeg", "Downloading"));
        if (failure is not null) throw failure;
        return answer;
    }
}

sealed class RecordingContext : SynchronizationContext
{
    [ThreadStatic] static bool _inside;

    /// <summary>True only while running work this context was handed, as the UI thread would.</summary>
    public static bool Inside => _inside;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _inside = true;
        try { d(state); }
        finally { _inside = false; }
    }
}

public class FirstRunThreadTests
{
    [Fact]
    public async Task Tool_progress_reports_come_back_to_the_thread_that_started_setup()
    {
        var before = SynchronizationContext.Current;
        var ui = new RecordingContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var status = new EngineStatusViewModel();
            int reports = 0, offThread = 0;
            status.PropertyChanged += (_, _) =>
            {
                Interlocked.Increment(ref reports);
                if (!RecordingContext.Inside) Interlocked.Increment(ref offThread);
            };
            var panel = new FirstRunViewModel(new OffThreadSetup(ToolHealth.Current), status);
            await Task.Run(() => { SynchronizationContext.SetSynchronizationContext(ui); return panel.StartAsync(); });
            await Task.Delay(50);
            Assert.True(reports > 0, "nothing was reported");
            Assert.Equal(0, offThread);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    [Fact]
    public async Task A_setup_that_fails_outright_shows_the_panel_with_retry()
    {
        var panel = new FirstRunViewModel(new OffThreadSetup(ToolHealth.Current, new IOException("disk full")), new EngineStatusViewModel());
        await panel.StartAsync();
        Assert.False(panel.IsReady);
        Assert.True(panel.IsVisible);
        Assert.True(panel.CanRetry);
        Assert.False(panel.IsBusy);
        Assert.DoesNotContain("disk full", panel.Message);
    }

    [Fact]
    public async Task Closing_during_setup_is_not_reported_as_a_failure()
    {
        var panel = new FirstRunViewModel(new OffThreadSetup(ToolHealth.Current, new OperationCanceledException()), new EngineStatusViewModel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => panel.StartAsync());
    }
}

public class PastedLinkTests : ConsoleFixture
{
    const string Signed = "https://cdn.example/a.mp4?X-Amz-Signature=abc&token=def";

    static LinkAnalysis OneEntry(string url) => LinkAnalysis.Found("A", [new PlaylistEntry("a", "A", url, true)]);

    [Fact]
    public async Task A_link_carrying_a_secret_downloads_from_the_working_link_held_in_memory()
    {
        var source = new SourceViewModel(new ScriptedAnalyzer(OneEntry), Catalog, Engine) { Link = Signed };
        await source.AnalyzeCommand.ExecuteAsync(null);
        var job = Assert.Single(Jobs.All());
        Assert.Equal(JobKind.Captured, job.Kind);
        Assert.Equal(Signed, Vault.Get(job.Id)!.StreamUrl);
    }

    [Fact]
    public async Task An_ordinary_link_stays_a_link_job()
    {
        var source = new SourceViewModel(new ScriptedAnalyzer(OneEntry), Catalog, Engine) { Link = "https://www.youtube.com/watch?v=abcdefghijk" };
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal(JobKind.Link, Assert.Single(Jobs.All()).Kind);
    }
}

public class DuplicateTests : ConsoleFixture
{
    static LinkAnalysis OneEntry(string url) => LinkAnalysis.Found("A", [new PlaylistEntry("a", "A", url, true)]);

    SourceViewModel Source(OutputChoice output, Func<string, bool>? exists = null) =>
        new(new ScriptedAnalyzer(OneEntry), Catalog, Engine, () => output, exists ?? (_ => true)) { Link = "https://x/a" };

    [Fact]
    public async Task A_second_format_of_a_saved_source_is_not_a_duplicate()
    {
        long media = Catalog.AddItem("https://x/a", "a", "A");
        Catalog.AddFile(media, @"D:\a.mkv", null, SourceViewModel.OriginalFormat);
        await Source(OutputChoice.Flac).AnalyzeCommand.ExecuteAsync(null);
        Assert.Single(Jobs.All());
    }

    [Fact]
    public async Task The_same_format_of_a_saved_source_is_a_duplicate()
    {
        long media = Catalog.AddItem("https://x/a", "a", "A");
        Catalog.AddFile(media, @"D:\a.mkv", null, SourceViewModel.OriginalFormat);
        var source = Source(OutputChoice.Original);
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Empty(Jobs.All());
        Assert.Contains("already saved", source.Message);
    }

    [Fact]
    public async Task A_saved_file_you_deleted_can_be_saved_again()
    {
        long media = Catalog.AddItem("https://x/a", "a", "A");
        Catalog.AddFile(media, @"D:\gone.mkv", null, SourceViewModel.OriginalFormat);
        await Source(OutputChoice.Original, _ => false).AnalyzeCommand.ExecuteAsync(null);
        Assert.Single(Jobs.All());
    }
}

public class VideoInPlaylistTests : ConsoleFixture
{
    const string Link = "https://www.youtube.com/watch?v=abcdefghijk&list=PL1&index=3";

    static LinkAnalysis Many(string url) => url.Contains("list=")
        ? LinkAnalysis.Found("P", [new PlaylistEntry("abcdefghijk", "A", "https://www.youtube.com/watch?v=abcdefghijk", true), new PlaylistEntry("b", "B", "https://www.youtube.com/watch?v=bbbbbbbbbbb", true)])
        : LinkAnalysis.Found("A", [new PlaylistEntry("abcdefghijk", "A", url, true)]);

    [Fact]
    public async Task A_video_link_with_a_playlist_offers_just_that_video()
    {
        var source = new SourceViewModel(new ScriptedAnalyzer(Many), Catalog, Engine) { Link = Link };
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", source.PendingPlaylist!.SingleVideoUrl);

        await source.JustThisVideoCommand.ExecuteAsync(null);
        Assert.Null(source.PendingPlaylist);
        var job = Assert.Single(Jobs.All());
        Assert.Equal("https://www.youtube.com/watch?v=abcdefghijk", Catalog.GetItem(job.MediaId)!.PageUrl);
    }

    [Theory]
    [InlineData("https://www.youtube.com/playlist?list=PL1")]
    [InlineData("https://www.youtube.com/channel/UC1/videos")]
    [InlineData("https://other.example/watch?v=abcdefghijk&list=PL1")]
    public void A_link_that_names_no_single_video_offers_none(string url) =>
        Assert.Null(SingleVideoLink.From(url));

    [Fact]
    public void Every_download_is_one_item_since_playlists_are_listed_first() =>
        Assert.Contains("--no-playlist", YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", @"D:\", DownloadMode.Best, null)));
}

public class QueueHistoryTests : ConsoleFixture
{
    [Fact]
    public void A_long_history_loads_only_recent_finished_jobs_and_every_unfinished_one()
    {
        var finished = Enumerable.Range(0, QueueViewModel.HistoryShown + 20).Select(_ => new Job(0, 1, JobKind.Link, JobState.Completed, 0, null, "x", DateTimeOffset.UnixEpoch));
        long media = Catalog.AddItem("https://x/a", "a", "A");
        var added = finished.Select(j => Jobs.Add(j with { MediaId = media })).ToList();
        var waiting = Jobs.Add(new Job(0, media, JobKind.Link, JobState.Paused, 0, null, "x", DateTimeOffset.UnixEpoch));

        Queue.Load();

        Assert.Equal(QueueViewModel.HistoryShown + 1, Queue.Items.Count);
        Assert.Contains(Queue.Items, i => i.JobId == waiting.Id);
        Assert.Contains(Queue.Items, i => i.JobId == added[^1].Id);
        Assert.DoesNotContain(Queue.Items, i => i.JobId == added[0].Id);
    }
}

public class OffThreadRunnerTests
{
    sealed class Blocking(ManualResetEventSlim gate) : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
        {
            gate.Wait(TimeSpan.FromSeconds(1), cancel);
            return Task.FromResult(JobOutcome.Success);
        }
    }

    [Fact]
    public async Task Starting_a_job_never_blocks_the_caller()
    {
        using var gate = new ManualResetEventSlim();
        var run = new OffThreadRunner(new Blocking(gate)).RunAsync(new Job(1, 1, JobKind.Link, JobState.Running, 0, null, "x", DateTimeOffset.UnixEpoch), CancellationToken.None);
        Assert.False(run.IsCompleted);
        gate.Set();
        Assert.True((await run).IsSuccess);
    }
}

public class DownloadChoiceTests
{
    [Theory]
    [InlineData(DownloadChoice.Best, DownloadMode.Best, null)]
    [InlineData(DownloadChoice.Up1080, DownloadMode.Best, 1080)]
    [InlineData(DownloadChoice.Up720, DownloadMode.Best, 720)]
    [InlineData(DownloadChoice.VideoOnly, DownloadMode.VideoOnly, null)]
    [InlineData(DownloadChoice.AudioOnly, DownloadMode.AudioOnly, null)]
    public void Each_choice_maps_to_what_yt_dlp_is_asked_for(DownloadChoice choice, DownloadMode mode, int? height) =>
        Assert.Equal((mode, height), DownloadChoices.Map(choice));

    [Fact]
    public void Every_choice_has_a_label() =>
        Assert.Equal(Enum.GetValues<DownloadChoice>().Length, SettingsViewModel.DownloadLabels.Count);

    [Fact]
    public void Metadata_switches_become_what_the_download_embeds()
    {
        var settings = new Stx.Core.Settings.AppSettings { EmbedTags = true, EmbedCoverArt = false, EmbedChapters = true };
        Assert.Equal(new MetadataOptions(true, false, true), DownloadChoices.Metadata(settings));
    }

    [Fact]
    public void Embedding_is_off_until_you_choose_it()
    {
        Assert.Equal(MetadataOptions.None, DownloadChoices.Metadata(new Stx.Core.Settings.AppSettings()));
    }
}
