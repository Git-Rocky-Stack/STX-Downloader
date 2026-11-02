using Stx.Core.Files;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Tools;
using Xunit;

namespace Stx.Tools.Tests;

/// <summary>Output lines here are copied from real yt-dlp 2026.10.26 runs: a second video given a name that was already taken.</summary>
public sealed class NameCollisionTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-collision").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    static Job Job(long id = 1) => new(id, 1, JobKind.Link, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch);

    // Answers like yt-dlp: a name already on disk is reported as already downloaded, with exit 0.
    sealed class FakeYtDlp(string root, params string[] alreadyOnDisk) : IProcessRunner
    {
        public readonly List<string> Templates = [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            string template = args[args.ToList().IndexOf("-o") + 1];
            Templates.Add(template);
            string file = Path.Combine(root, template.Replace("%(id)s", "ID2").Replace(".%(ext)s", ".mp4"));
            if (alreadyOnDisk.Contains(file)) onLine($"[download] {file} has already been downloaded");
            else onLine($"[download] Destination: {file}");
            onLine("[download] 100% of   29.77KiB");
            onLine(YtDlpArgs.FileMarker + file);
            return Task.FromResult(0);
        }
    }

    YtDlpRunner Runner(IProcessRunner process, Func<string, bool>? own = null) =>
        new(process, "yt-dlp.exe", _ => new DownloadRequest("https://x/y", _root, DownloadMode.Best, null)
        {
            OutputTemplate = "clip",
            UniqueTemplate = FilenameTemplate.UniqueVariant("clip"),
            IsOwnFile = own,
        }, new CaptureVault(), _root);

    [Fact]
    public async Task A_name_taken_by_another_video_downloads_again_under_a_unique_name()
    {
        string taken = Path.Combine(_root, "clip.mp4");
        var process = new FakeYtDlp(_root, taken);
        var saved = new List<SavedFile>();
        var runner = Runner(process);
        runner.FileSaved += (_, file) => saved.Add(file);

        var outcome = await runner.RunAsync(Job(), default);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(["clip.%(ext)s", "clip [%(id)s].%(ext)s"], process.Templates);
        var only = Assert.Single(saved);
        Assert.Equal(Path.Combine(_root, "clip [ID2].mp4"), only.Path);
        Assert.False(only.Reused);
    }

    [Fact]
    public async Task The_same_videos_own_file_is_kept_without_a_second_download()
    {
        string own = Path.Combine(_root, "clip.mp4");
        var process = new FakeYtDlp(_root, own);
        var saved = new List<SavedFile>();
        var runner = Runner(process, path => path == own);
        runner.FileSaved += (_, file) => saved.Add(file);

        await runner.RunAsync(Job(), default);

        Assert.Single(process.Templates);
        Assert.Equal(new SavedFile(1, own, Reused: true), Assert.Single(saved));
    }

    [Fact]
    public async Task A_free_name_runs_once()
    {
        var process = new FakeYtDlp(_root);
        var saved = new List<SavedFile>();
        var runner = Runner(process);
        runner.FileSaved += (_, file) => saved.Add(file);

        await runner.RunAsync(Job(), default);

        Assert.Single(process.Templates);
        Assert.False(Assert.Single(saved).Reused);
    }

    [Fact]
    public void A_template_with_an_id_has_no_unique_variant()
    {
        Assert.Null(FilenameTemplate.UniqueVariant(FilenameTemplate.Default));
        Assert.Null(FilenameTemplate.UniqueVariant("{ID} - {title}"));
        Assert.Null(FilenameTemplate.UniqueVariant("   "));
        Assert.Equal("{title} [{id}]", FilenameTemplate.UniqueVariant("{title}"));
    }
}

public sealed class Mp4PreferenceTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-mp4").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    const string Ffmpeg = @"C:\tools\ffmpeg.exe";

    sealed class Fake(string probeOutput, int remuxExit = 0) : IProcessRunner
    {
        public IReadOnlyList<string>? RemuxArgs;
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            if (path.EndsWith("ffprobe.exe", StringComparison.Ordinal))
            {
                Assert.Equal(@"C:\tools\ffprobe.exe", path);
                foreach (string line in probeOutput.Split('\n')) onLine(line);
                return Task.FromResult(0);
            }
            RemuxArgs = args;
            if (remuxExit == 0) File.WriteAllText(args[^1], "remuxed");
            return Task.FromResult(remuxExit);
        }
    }

    string Mkv()
    {
        string path = Path.Combine(_root, "talk.mkv");
        File.WriteAllText(path, "original");
        return path;
    }

    [Fact]
    public async Task H264_and_AAC_move_to_MP4_by_stream_copy()
    {
        string mkv = Mkv();
        var process = new Fake("h264,video\naac,audio");

        string result = await new Mp4Preference(process).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(Path.Combine(_root, "talk.mp4"), result);
        Assert.Equal("remuxed", File.ReadAllText(result));
        Assert.False(File.Exists(mkv));
        Assert.Contains("copy", process.RemuxArgs!);
        Assert.DoesNotContain("libx264", process.RemuxArgs!);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("vp9,video\nopus,audio")]
    [InlineData("h264,video\nopus,audio")]
    [InlineData("h264,video\naac,audio\nsubrip,subtitle")]
    [InlineData("h264,video\naac,audio\nmjpeg,video")]
    [InlineData("h264,video\naac,audio\n,attachment")]
    [InlineData("aac,audio")]
    public async Task Anything_MP4_might_not_hold_stays_MKV_untouched(string streams)
    {
        string mkv = Mkv();
        var process = new Fake(streams);

        string result = await new Mp4Preference(process).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.Null(process.RemuxArgs);
        Assert.Equal("original", File.ReadAllText(mkv));
    }

    [Fact]
    public async Task A_failed_remux_keeps_the_MKV_and_leaves_no_partial()
    {
        string mkv = Mkv();
        string result = await new Mp4Preference(new Fake("h264,video\naac,audio", remuxExit: 1)).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.Equal("original", File.ReadAllText(mkv));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task An_existing_MP4_of_that_name_is_never_replaced()
    {
        string mkv = Mkv();
        string other = Path.Combine(_root, "talk.mp4");
        File.WriteAllText(other, "someone else's");

        string result = await new Mp4Preference(new Fake("h264,video\naac,audio")).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.Equal("someone else's", File.ReadAllText(other));
    }

    [Fact]
    public async Task A_file_that_is_already_MP4_is_left_alone()
    {
        string mp4 = Path.Combine(_root, "talk.mp4");
        File.WriteAllText(mp4, "original");
        var process = new Fake("h264,video\naac,audio");

        Assert.Equal(mp4, await new Mp4Preference(process).PreferAsync(mp4, Ffmpeg));
        Assert.Null(process.RemuxArgs);
    }

    [Fact]
    public async Task The_runner_reports_the_MP4_it_ended_up_with()
    {
        string mkv = Mkv();
        var remuxing = new Fake("h264,video\naac,audio");
        var process = new ScriptedThenRemux(remuxing, YtDlpArgs.FileMarker + mkv);
        var saved = new List<SavedFile>();
        var runner = new YtDlpRunner(process, "yt-dlp.exe", _ => new DownloadRequest("https://x/y", _root, DownloadMode.Best, null) { Tools = new ToolPaths(Ffmpeg, null) }, new CaptureVault(), _root);
        runner.FileSaved += (_, file) => saved.Add(file);

        await runner.RunAsync(new Job(1, 1, JobKind.Link, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch), default);

        Assert.Equal(Path.Combine(_root, "talk.mp4"), Assert.Single(saved).Path);
    }

    sealed class ScriptedThenRemux(IProcessRunner rest, string line) : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            if (path != "yt-dlp.exe") return rest.RunAsync(path, args, onLine, cancel);
            onLine(line);
            return Task.FromResult(0);
        }
    }
}

public sealed class ConversionLogTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-convlog").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    sealed class Talking : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            onLine("Stream #0:0: Audio: flac, 48000 Hz");
            onLine("out_time_ms=1000000");
            onLine("speed=12.3x");
            File.WriteAllText(args[^1], "converted");
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task What_ffmpeg_says_reaches_the_log_without_its_progress_fields()
    {
        string input = Path.Combine(_root, "song.webm");
        File.WriteAllText(input, "original");
        var logged = new List<string>();

        await new FfmpegRunner(new Talking(), "ffmpeg.exe").ConvertAsync(new ConvertRequest(input, _root, Stx.Core.Audio.OutputChoice.Flac), null, default, logged.Add);

        Assert.Equal(["Stream #0:0: Audio: flac, 48000 Hz"], logged);
    }
}
