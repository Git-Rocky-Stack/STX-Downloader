using Stx.Core.Audio;
using Stx.Core.Files;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Tools;
using Xunit;

namespace Stx.Tools.Tests;

/// <summary>What happens when a download finds an earlier one's file, however yt-dlp happens to word it.</summary>
public sealed class RepeatDownloadTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-repeat").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    static Job Job() => new(1, 1, JobKind.Link, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch);

    // Reports the file for its first template and writes nothing, in words no parser knows.
    sealed class Reworded(string file, string unique) : IProcessRunner
    {
        public readonly List<string> Templates = [];
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            Templates.Add(args[args.ToList().IndexOf("-o") + 1]);
            onLine("[download] Skipping, a file by that name exists");
            onLine(YtDlpArgs.FileMarker + (Templates.Count == 1 ? file : unique));
            return Task.FromResult(0);
        }
    }

    YtDlpRunner Runner(IProcessRunner process) =>
        new(process, "yt-dlp.exe", _ => new DownloadRequest("https://x/y", _root, DownloadMode.Best, null)
        {
            OutputTemplate = "clip",
            UniqueTemplate = FilenameTemplate.UniqueVariant("clip"),
        }, new CaptureVault(), _root);

    [Fact]
    public async Task A_file_older_than_the_run_is_a_taken_name_even_when_the_tool_rewords_itself()
    {
        string taken = Path.Combine(_root, "clip.mp4");
        File.WriteAllText(taken, "another video");
        File.SetLastWriteTimeUtc(taken, DateTime.UtcNow.AddHours(-1));
        var process = new Reworded(taken, Path.Combine(_root, "clip [ID2].mp4"));

        await Runner(process).RunAsync(Job(), default);

        Assert.Equal(["clip.%(ext)s", "clip [%(id)s].%(ext)s"], process.Templates);
    }

    [Fact]
    public async Task A_file_written_during_the_run_is_not_a_taken_name()
    {
        string fresh = Path.Combine(_root, "clip.mp4");
        File.WriteAllText(fresh, "this video");
        var process = new Reworded(fresh, "unused");
        var saved = new List<SavedFile>();
        var runner = Runner(process);
        runner.FileSaved += (_, file) => saved.Add(file);

        await runner.RunAsync(Job(), default);

        Assert.Single(process.Templates);
        Assert.False(Assert.Single(saved).Reused);
    }

    [Fact]
    public void Saved_files_are_dated_when_they_were_saved_because_that_is_what_tells_them_apart() =>
        Assert.Contains("--no-mtime", YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", _root, DownloadMode.Best, null)));

    const string Ffmpeg = @"C:\tools\ffmpeg.exe";

    sealed class Hashes(Func<string, string?> hashOf) : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            if (hashOf(args[args.ToList().IndexOf("-i") + 1]) is not { } hash) return Task.FromResult(1);
            onLine($"0,v,SHA256={hash}");
            return Task.FromResult(0);
        }
    }

    (string Mkv, string Mp4) Both()
    {
        string mkv = Path.Combine(_root, "talk.mkv"), mp4 = Path.Combine(_root, "talk.mp4");
        File.WriteAllText(mkv, "new download");
        File.WriteAllText(mp4, "earlier download");
        return (mkv, mp4);
    }

    [Fact]
    public async Task The_same_streams_already_moved_to_MP4_keep_the_MP4_alone()
    {
        var (mkv, mp4) = Both();

        string result = await new Mp4Preference(new Hashes(_ => "abc")).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mp4, result);
        Assert.False(File.Exists(mkv));
        Assert.Equal("earlier download", File.ReadAllText(mp4));
    }

    [Fact]
    public async Task A_different_video_that_shares_the_name_keeps_both_files()
    {
        var (mkv, mp4) = Both();

        string result = await new Mp4Preference(new Hashes(path => path == mkv ? "abc" : "def")).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.True(File.Exists(mkv));
        Assert.True(File.Exists(mp4));
    }

    [Fact]
    public async Task Streams_that_cannot_be_read_keep_both_files()
    {
        var (mkv, mp4) = Both();

        string result = await new Mp4Preference(new Hashes(path => path == mkv ? "abc" : null)).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.True(File.Exists(mkv));
        Assert.True(File.Exists(mp4));
    }

    // Reports the same hash, then fails: it may only have read part of the file, so that match means nothing.
    sealed class FailsPartWay(string failing) : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            onLine("0,v,SHA256=abc");
            return Task.FromResult(args[args.ToList().IndexOf("-i") + 1] == failing ? 1 : 0);
        }
    }

    [Fact]
    public async Task A_read_that_fails_part_way_keeps_both_files()
    {
        var (mkv, mp4) = Both();

        string result = await new Mp4Preference(new FailsPartWay(mp4)).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.True(File.Exists(mkv));
        Assert.True(File.Exists(mp4));
    }

    [Fact]
    public async Task Nothing_to_compare_keeps_both_files()
    {
        var (mkv, mp4) = Both();

        string result = await new Mp4Preference(new Hashes(_ => null)).PreferAsync(mkv, Ffmpeg);

        Assert.Equal(mkv, result);
        Assert.True(File.Exists(mkv));
        Assert.True(File.Exists(mp4));
    }

    sealed class StatesItsLength(string duration) : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            onLine($"  Duration: {duration}, start: 0.000000, bitrate: 128 kb/s");
            onLine("out_time_ms=30000000");
            File.WriteAllText(args[^1], "converted");
            return Task.FromResult(0);
        }
    }

    [Theory]
    [InlineData("00:01:00.00", 50)]
    [InlineData("01:00:30.00", 30.0 / 3630 * 100)]
    public async Task A_conversion_works_out_its_percent_from_the_length_ffmpeg_states(string duration, double expected)
    {
        string input = Path.Combine(_root, "song.webm");
        File.WriteAllText(input, "original");
        var seen = new List<double>();

        await new FfmpegRunner(new StatesItsLength(duration), "ffmpeg.exe").ConvertAsync(new ConvertRequest(input, _root, OutputChoice.Flac), seen.Add);

        Assert.Equal(expected, Assert.Single(seen), 3);
    }

    [Fact]
    public async Task A_length_ffmpeg_cannot_state_shows_no_percent()
    {
        string input = Path.Combine(_root, "song.webm");
        File.WriteAllText(input, "original");
        var seen = new List<double>();

        await new FfmpegRunner(new StatesItsLength("N/A"), "ffmpeg.exe").ConvertAsync(new ConvertRequest(input, _root, OutputChoice.Flac), seen.Add);

        Assert.Empty(seen);
    }
}
