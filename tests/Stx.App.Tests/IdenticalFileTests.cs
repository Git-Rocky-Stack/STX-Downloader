using Stx.Core.Jobs;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

public sealed class IdenticalFileCheckTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-identical").FullName;
    readonly List<MediaFact> _facts = [];
    readonly Dictionary<long, (string Hash, string Path)> _recorded = [];

    public void Dispose() => Directory.Delete(_root, true);

    string Write(string name, string body)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, body);
        return path;
    }

    IdenticalFileCheck Check(Action<string>? delete = null) => new(() => _facts, (id, hash, path) =>
    {
        _recorded[id] = (hash, path);
        _facts.Add(new MediaFact(null, hash, "Original", path, id));
    }, File.Exists, delete ?? File.Delete);

    [Fact]
    public async Task The_same_bytes_from_another_link_keep_the_first_file_only()
    {
        string first = Write("first.mp4", "same bytes");
        string second = Write("second.mp4", "same bytes");
        var check = Check();
        await check.SettleAsync(new SavedMedia(1, 1, first, "First"));

        var settled = await check.SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(first, settled.Path);
        Assert.True(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal(first, _recorded[2].Path);
        Assert.Equal(_recorded[1].Hash, _recorded[2].Hash);
    }

    [Fact]
    public async Task Different_bytes_are_both_kept_with_their_own_hashes()
    {
        string first = Write("first.mp4", "one");
        string second = Write("second.mp4", "two");
        var check = Check();
        await check.SettleAsync(new SavedMedia(1, 1, first, "First"));

        var settled = await check.SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(second, settled.Path);
        Assert.True(File.Exists(second));
        Assert.NotEqual(_recorded[1].Hash, _recorded[2].Hash);
        Assert.Equal(64, _recorded[2].Hash.Length);
    }

    [Fact]
    public async Task A_file_saved_before_hashes_were_kept_still_counts_as_the_first_copy()
    {
        string older = Write("older.mp4", "same bytes");
        string second = Write("second.mp4", "same bytes");
        _facts.Add(new MediaFact(null, null, "Original", older, 1, FileId: 1));

        var settled = await Check().SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(older, settled.Path);
        Assert.False(File.Exists(second));
        Assert.Equal(older, _recorded[1].Path);
    }

    [Fact]
    public async Task An_older_file_of_another_length_is_not_read_at_all()
    {
        string older = Write("older.mp4", "something longer");
        string second = Write("second.mp4", "short");
        _facts.Add(new MediaFact(null, null, "Original", older, 1, FileId: 1));

        var settled = await Check().SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(second, settled.Path);
        Assert.False(_recorded.ContainsKey(1));
    }

    [Fact]
    public async Task A_file_the_job_found_already_there_is_never_removed()
    {
        string first = Write("first.mp4", "same bytes");
        string yours = Write("yours.mp4", "same bytes");
        var check = Check();
        await check.SettleAsync(new SavedMedia(1, 1, first, "First"));

        var settled = await check.SettleAsync(new SavedMedia(2, 2, yours, "Yours") { WasAlreadyThere = true });

        Assert.Equal(yours, settled.Path);
        Assert.True(File.Exists(yours));
    }

    [Fact]
    public async Task A_match_whose_file_is_gone_does_not_count()
    {
        string gone = Write("gone.mp4", "same bytes");
        string second = Write("second.mp4", "same bytes");
        var check = Check();
        await check.SettleAsync(new SavedMedia(1, 1, gone, "Gone"));
        File.Delete(gone);

        var settled = await check.SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(second, settled.Path);
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task A_second_row_for_one_file_is_not_its_own_duplicate()
    {
        string file = Write("file.mp4", "bytes");
        var check = Check();
        await check.SettleAsync(new SavedMedia(1, 1, file, "One"));

        var settled = await check.SettleAsync(new SavedMedia(2, 2, file, "Two"));

        Assert.Equal(file, settled.Path);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task A_copy_that_cannot_be_deleted_stays_recorded_where_it_is()
    {
        string first = Write("first.mp4", "same bytes");
        string second = Write("second.mp4", "same bytes");
        var check = Check(_ => throw new IOException("in use"));
        await check.SettleAsync(new SavedMedia(1, 1, first, "First"));

        var settled = await check.SettleAsync(new SavedMedia(2, 2, second, "Second"));

        Assert.Equal(second, settled.Path);
        Assert.Equal(second, _recorded[2].Path);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_is_left_as_it_is()
    {
        string gone = Path.Combine(_root, "never-there.mp4");
        var settled = await Check().SettleAsync(new SavedMedia(1, 1, gone, "Gone"));
        Assert.Equal(gone, settled.Path);
        Assert.Empty(_recorded);
    }

    [Fact]
    public void A_row_marked_already_saved_says_so_when_complete()
    {
        var row = new QueueItemViewModel(1, "Song", null);
        row.MarkAlreadySaved();
        row.Apply(new Job(1, 1, JobKind.Link, JobState.Completed, 0, null, "original", DateTimeOffset.UnixEpoch));
        Assert.Equal("COMPLETE · SAME FILE ALREADY SAVED", row.Status);

        var plain = new QueueItemViewModel(2, "Song", null);
        plain.Apply(new Job(2, 1, JobKind.Link, JobState.Completed, 0, null, "original", DateTimeOffset.UnixEpoch));
        Assert.Equal("COMPLETE", plain.Status);
    }
}

public sealed class ToolsOffMessageTests
{
    sealed class Missing : IToolSetup
    {
        public Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default) => Task.FromResult(ToolHealth.Missing);
    }

    [Fact]
    public async Task With_updates_off_the_message_names_the_setting_not_the_connection()
    {
        var panel = new FirstRunViewModel(new Missing(), new EngineStatusViewModel(), () => false);
        await panel.StartAsync();
        Assert.Contains("Settings", panel.Message);
        Assert.DoesNotContain("connection", panel.Message);
        Assert.True(panel.CanRetry);
    }

    [Fact]
    public async Task With_updates_on_a_failure_still_points_at_the_connection()
    {
        var panel = new FirstRunViewModel(new Missing(), new EngineStatusViewModel(), () => true);
        await panel.StartAsync();
        Assert.Contains("connection", panel.Message);
    }
}

public sealed class ConversionJobLogTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-joblog").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    sealed class Talking : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            onLine("Stream mapping: 0:a -> flac");
            File.WriteAllText(args[^1], "converted");
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task A_conversions_output_is_logged_against_its_job()
    {
        string input = Path.Combine(_root, "song.webm");
        File.WriteAllText(input, "original");
        var runner = new ConversionRunner(new FfmpegRunner(new Talking(), "ffmpeg.exe"), _ => input, (_, _, _) => 9);
        var logged = new List<JobLogLine>();
        SavedMedia? saved = null;
        runner.LogLine += (_, line) => logged.Add(line);
        runner.FileSaved += (_, media) => saved = media;

        await runner.RunAsync(new Job(42, 5, JobKind.Convert, JobState.Running, 0, null, "convert:Flac:7", DateTimeOffset.UnixEpoch), default);

        Assert.Equal(new JobLogLine(42, "Stream mapping: 0:a -> flac"), Assert.Single(logged));
        Assert.Equal(42L, saved!.JobId);
    }

    sealed class Halfway : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            onLine("  Duration: 00:01:00.00, start: 0.000000, bitrate: 128 kb/s");
            onLine("out_time_ms=30000000");
            File.WriteAllText(args[^1], "converted");
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task A_conversion_reports_how_far_along_it_is_against_its_job()
    {
        string input = Path.Combine(_root, "song.webm");
        File.WriteAllText(input, "original");
        var runner = new ConversionRunner(new FfmpegRunner(new Halfway(), "ffmpeg.exe"), _ => input, (_, _, _) => 9);
        var seen = new List<JobProgress>();
        runner.ProgressChanged += (_, update) => seen.Add(update);

        await runner.RunAsync(new Job(42, 5, JobKind.Convert, JobState.Running, 0, null, "convert:Flac:7", DateTimeOffset.UnixEpoch), default);

        var only = Assert.Single(seen);
        Assert.Equal(42L, only.JobId);
        Assert.Equal(50, only.Progress.Percent);
    }

    [Theory]
    [InlineData(JobKind.Convert, "CONVERTING")]
    [InlineData(JobKind.Link, "DOWNLOADING")]
    public void A_rows_status_says_what_kind_of_work_it_is(JobKind kind, string expected)
    {
        var row = new QueueItemViewModel(1, "Song", null);
        row.Apply(new Job(1, 1, kind, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch));

        row.Show(new Progress(50, null, null, null));

        Assert.Equal(expected, row.Status);
        Assert.Equal(50, row.Percent);
    }
}
