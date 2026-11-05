using System.Diagnostics;
using System.Security.Cryptography;
using Stx.Core.Audio;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Library;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>Phase 7: shrink presets, their arguments, what the Library says about them, and the folder job's choices.</summary>
public class ShrinkTests
{
    static Preset Named(string name) => PresetCatalog.All.Single(p => p.Name == name);

    [Fact]
    public void Three_shrink_presets_name_their_codec_and_their_copy()
    {
        Assert.Equal((ShrinkCodec?)ShrinkCodec.Av1, Named("Shrink AV1").Shrink);
        Assert.Equal(" (AV1)", Named("Shrink AV1").Suffix);
        Assert.Equal((ShrinkCodec?)ShrinkCodec.Hevc, Named("Shrink HEVC").Shrink);
        Assert.Equal((ShrinkCodec?)ShrinkCodec.Opus, Named("Shrink Opus").Shrink);
        Assert.Equal(OutputChoice.Opus, Named("Shrink Opus").Output);
    }

    // Source Truth labels shrink copies lossy, never improved.
    [Fact]
    public void Every_shrink_copy_is_called_lossy()
    {
        foreach (var preset in PresetCatalog.All.Where(p => p.Shrink is not null))
        {
            Assert.Equal("Re-encoded; slight quality loss", preset.VerdictFor(sourceIsLossy: false));
            Assert.Contains("Lossy", preset.Description);
            Assert.DoesNotContain("better", preset.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Sound_alone_goes_to_opus_and_pictures_to_the_chosen_codec()
    {
        Assert.Equal("Shrink Opus", PresetCatalog.ShrinkFor(hasVideo: false, ShrinkCodec.Hevc).Name);
        Assert.Equal("Shrink HEVC", PresetCatalog.ShrinkFor(hasVideo: true, ShrinkCodec.Hevc).Name);
        Assert.Equal("Shrink AV1", PresetCatalog.ShrinkFor(hasVideo: true, ShrinkCodec.Av1).Name);
    }

    [Fact]
    public void Av1_keeps_every_track_in_ten_bits_and_never_overwrites()
    {
        var args = FfmpegArgs.ForConvert(new ConvertRequest(@"D:\Films\Big Night.mkv", @"D:\Films", OutputChoice.Mkv) { Shrink = ShrinkCodec.Av1, Metadata = new(true, true, true) }, "out.mkv", "libx264");
        string line = string.Join(' ', args);
        Assert.Contains("-n", args);
        Assert.Contains("-c:v libsvtav1", line);
        Assert.Contains("-pix_fmt yuv420p10le", line);
        Assert.Contains("-c:a libopus", line);
        Assert.Contains("-map 0:s? -map 0:t?", line);
        Assert.Contains("-c:s copy", line);
        Assert.Contains("-map_chapters 0", line);
        Assert.Equal("out.mkv", args[^1]);
    }

    [Fact]
    public void Mp4_subtitles_become_srt_because_mkv_cant_hold_them()
    {
        var args = FfmpegArgs.ForConvert(new ConvertRequest(@"D:\clip.mp4", @"D:\", OutputChoice.Mkv) { Shrink = ShrinkCodec.Hevc }, "out.mkv", "libx264");
        Assert.Contains("-c:v libx265", string.Join(' ', args));
        Assert.Contains("-c:s srt", string.Join(' ', args));
    }

    [Fact]
    public void Opus_takes_the_sound_alone()
    {
        string line = string.Join(' ', FfmpegArgs.ForConvert(new ConvertRequest(@"D:\a.flac", @"D:\", OutputChoice.Opus) { Shrink = ShrinkCodec.Opus }, "out.opus", "libx264"));
        Assert.Contains("-map 0:a -vn", line);
        Assert.Contains("-c:a libopus -b:a 128k", line);
        Assert.DoesNotContain("-c:v", line);
    }

    // Space saved is shown, and STX never deletes the original.
    [Fact]
    public void A_shrink_copy_says_how_much_smaller_it_is_and_the_library_adds_it_up()
    {
        var catalog = new LibraryCatalog();
        catalog.Stored.Add(new LibraryItem(1, "Big Night", null, [new LibraryFile(1, @"D:\f\Big Night.mkv", "Original"), new LibraryFile(2, @"D:\f\Big Night (AV1).mkv", "Shrink AV1")], MediaOrigin.Watched));
        var sizes = new Dictionary<string, long> { [@"D:\f\Big Night.mkv"] = 4L << 30, [@"D:\f\Big Night (AV1).mkv"] = 1L << 30 };
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(catalog, engine, new ShellSpy(), _ => true, _ => { }, p => sizes.GetValueOrDefault(p));
        library.Refresh();

        var copy = library.Rows.Single(r => r.FileId == 2);
        Assert.Equal("MKV · SHRINK AV1 · 75% SMALLER", copy.Status);
        Assert.Equal("DELETING 1 ORIGINAL WOULD FREE 3.0 GB. STX NEVER DELETES THEM.", library.SpaceText);
        Assert.Equal("MKV · WATCHED", library.Rows.Single(r => r.FileId == 1).Status);
    }

    [Fact]
    public void A_copy_is_never_shrunk_again_nor_an_original_twice()
    {
        var catalog = new LibraryCatalog();
        catalog.Stored.Add(new LibraryItem(1, "Big Night", null, [new LibraryFile(1, @"D:\f\a.mkv", "Original"), new LibraryFile(2, @"D:\f\a (AV1).mkv", "Shrink AV1")]));
        var jobs = new MemoryJobs();
        var engine = new QueueEngine(jobs, new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(catalog, engine, new ShellSpy(), _ => true, _ => { });
        library.Refresh();

        Assert.False(library.QueueShrink(library.Rows.Single(r => r.FileId == 2)));
        Assert.Equal("That file is already a shrink copy.", library.Message);
        Assert.False(library.QueueShrink(library.Rows.Single(r => r.FileId == 1)));
        library.ShrinkVideo = ShrinkCodec.Hevc;
        Assert.True(library.QueueShrink(library.Rows.Single(r => r.FileId == 1)));
        Assert.Equal("convert:Mkv:1:Shrink HEVC", Assert.Single(jobs.All()).Preset);
    }
}

/// <summary>
/// Phase 7 with the real FFmpeg. Done when: a folder job shrinks every file, originals are byte-identical before and
/// after, and a failed encode leaves no half-written copy marked done. Needs ffmpeg and ffprobe on PATH.
/// </summary>
[Trait("Category", "Ffmpeg")]
public sealed class ShrinkWithFfmpegTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-shrink").FullName;
    static readonly string Ffmpeg = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg", Ffprobe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    public void Dispose() => Directory.Delete(_dir, true);

    static string Tool(string name) =>
        Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException(name + " isn't on PATH. Install FFmpeg to run the Ffmpeg tests.");

    string Make(string name, string args)
    {
        string path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var start = new ProcessStartInfo(Tool(Ffmpeg)) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string arg in $"-hide_banner -loglevel error -y {args}".Split(' ')) start.ArgumentList.Add(arg);
        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        return path;
    }

    string Video(string name) => Make(name, "-f lavfi -i testsrc2=size=320x180:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 3 -c:v libx264 -pix_fmt yuv420p -c:a aac");
    string Song(string name) => Make(name, "-f lavfi -i sine=frequency=440:sample_rate=44100 -t 3 -c:a flac");

    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    FfmpegRunner Runner(CopyCheck? check = null, Func<string, Task<bool>>? hasEncoder = null)
    {
        var process = new ProcessRunner();
        var probe = new EncoderProbe(process, Tool(Ffmpeg));
        return new FfmpegRunner(process, Tool(Ffmpeg), null, check ?? new CopyCheck(process, Tool(Ffmpeg), Tool(Ffprobe)), hasEncoder ?? probe.HasAsync);
    }

    static string Codecs(string path)
    {
        var start = new ProcessStartInfo(Tool(Ffprobe)) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string arg in new[] { "-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        string text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return string.Join(',', text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    [Theory]
    [InlineData(ShrinkCodec.Av1, "av1,opus")]
    [InlineData(ShrinkCodec.Hevc, "hevc,opus")]
    public async Task A_copy_is_written_beside_the_original_and_the_original_is_untouched(ShrinkCodec codec, string codecs)
    {
        string original = Video("Big Night.mp4");
        string before = Hash(original);
        var preset = PresetCatalog.ShrinkFor(true, codec);
        var result = await Runner().ConvertAsync(new ConvertRequest(original, _dir, preset.Output) { Shrink = codec, Suffix = preset.Suffix, Metadata = preset.Metadata });

        Assert.True(result.IsSuccess, $"{result.Error}");
        Assert.Equal(Path.Combine(_dir, $"Big Night{preset.Suffix}.mkv"), result.OutputPath);
        Assert.Equal(codecs, Codecs(result.OutputPath!));
        Assert.Equal(before, Hash(original));
        Assert.Empty(Directory.GetFiles(_dir, FfmpegRunner.PartialPrefix + "*"));
    }

    [Fact]
    public async Task A_copy_that_fails_its_check_is_deleted_and_never_marked_done()
    {
        string original = Video("clip.mp4");
        string before = Hash(original);
        // A check that finds the copy a second short, as a cut-off encode would be.
        var check = new CopyCheck(new ShortProbe(new ProcessRunner()), Tool(Ffmpeg), Tool(Ffprobe));
        var result = await Runner(check).ConvertAsync(new ConvertRequest(original, _dir, OutputChoice.Mkv) { Shrink = ShrinkCodec.Hevc, Suffix = " (HEVC)" });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.CopyFailedCheck, result.Error);
        Assert.Equal([original], Directory.GetFiles(_dir));
        Assert.Equal(before, Hash(original));
    }

    [Fact]
    public async Task A_broken_original_leaves_nothing_behind()
    {
        string original = Video("broken.mp4");
        var bytes = File.ReadAllBytes(original);
        File.WriteAllBytes(original, bytes[..(bytes.Length / 3)]);
        string before = Hash(original);
        var result = await Runner().ConvertAsync(new ConvertRequest(original, _dir, OutputChoice.Mkv) { Shrink = ShrinkCodec.Av1, Suffix = " (AV1)" });

        Assert.False(result.IsSuccess);
        Assert.Equal([original], Directory.GetFiles(_dir));
        Assert.Equal(before, Hash(original));
    }

    // Spec assumption A7: the FFmpeg build has the encoders. If it doesn't, nothing is written and the job says why.
    [Fact]
    public async Task A_missing_encoder_writes_nothing()
    {
        string original = Video("clip.mp4");
        var result = await Runner(hasEncoder: _ => Task.FromResult(false)).ConvertAsync(new ConvertRequest(original, _dir, OutputChoice.Mkv) { Shrink = ShrinkCodec.Av1 });
        Assert.Equal(ErrorKind.EncoderMissing, result.Error);
        Assert.Equal([original], Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task This_ffmpeg_has_every_shrink_encoder() =>
        Assert.All(Enum.GetValues<ShrinkCodec>(), codec => Assert.True(new EncoderProbe(new ProcessRunner(), Tool(Ffmpeg)).HasAsync(FfmpegArgs.Encoder(codec)).Result, codec.ToString()));

    // Done when: a folder job shrinks every file, and originals are byte-identical before and after.
    [Fact]
    public async Task A_folder_job_shrinks_every_file_and_changes_no_original()
    {
        string[] originals = [Video("one.mp4"), Video(Path.Combine("Season 1", "two.mkv")), Song("song.flac")];
        var before = originals.ToDictionary(p => p, Hash);

        var catalog = new SharedCatalog();
        var jobs = new MemoryJobs();
        var conversions = new ConversionRunner(Runner(), id => id >= 1 && id <= catalog.Files.Count ? catalog.Files[(int)id - 1].Path : null,
            (media, path, format) => catalog.AddFile(media, path, null, format));
        var engine = new QueueEngine(jobs, new JobRouter(new HandRunner(), conversions), new QueuePolicy { MaxParallel = 2 }, new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(catalog, engine, new ShellSpy(), File.Exists, _ => { });
        library.Refresh();

        Assert.Equal(3, await new ShrinkFolder(catalog, library).QueueAsync(_dir));
        var clock = Stopwatch.StartNew();
        while (engine.AllJobs().Any(j => j.State is not (JobState.Completed or JobState.Failed or JobState.Canceled)))
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(3), "The folder job didn't finish.");
            engine.Tick();
            await Task.Delay(100);
        }

        Assert.All(engine.AllJobs(), j => Assert.Equal(JobState.Completed, j.State));
        Assert.True(File.Exists(Path.Combine(_dir, "one (AV1).mkv")));
        Assert.True(File.Exists(Path.Combine(_dir, "Season 1", "two (AV1).mkv")));
        Assert.True(File.Exists(Path.Combine(_dir, "song (Opus).opus")));
        foreach (var (path, hash) in before) Assert.Equal(hash, Hash(path));
        Assert.Empty(Directory.GetFiles(_dir, FfmpegRunner.PartialPrefix + "*", SearchOption.AllDirectories));

        // A second pass finds nothing left to do.
        library.Refresh();
        Assert.Equal(0, await new ShrinkFolder(catalog, library).QueueAsync(_dir));
    }

    /// <summary>Runs the tools for real, but reports any copy's length a second short.</summary>
    sealed class ShortProbe(IProcessRunner inner) : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            bool copy = args.Any(a => Path.GetFileName(a).StartsWith(FfmpegRunner.PartialPrefix, StringComparison.Ordinal));
            return inner.RunAsync(path, args, line =>
                onLine(copy && double.TryParse(line.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s)
                    ? (s - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : line), cancel);
        }
    }
}
