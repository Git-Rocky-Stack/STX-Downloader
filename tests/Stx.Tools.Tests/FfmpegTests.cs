using Stx.Core.Audio;
using Stx.Core.Errors;
using Stx.Core.Presets;
using Stx.Tools;
using Xunit;

namespace Stx.Tools.Tests;

public class FfmpegArgsTests
{
    static ConvertRequest Audio(OutputChoice output) => new(@"D:\M\song.webm", @"D:\M", output);

    static IReadOnlyList<string> Args(ConvertRequest request, string target = @"D:\M\song.partial.flac", string encoder = "libx264") =>
        FfmpegArgs.ForConvert(request, target, encoder);

    static string After(IReadOnlyList<string> args, string flag)
    {
        int at = args.ToList().IndexOf(flag);
        Assert.True(at >= 0, $"{flag} missing from {string.Join(' ', args)}");
        return args[at + 1];
    }

    [Theory]
    [InlineData(OutputChoice.Flac, "flac", ".flac")]
    [InlineData(OutputChoice.Alac, "alac", ".m4a")]
    [InlineData(OutputChoice.Wav, "pcm_s24le", ".wav")]
    [InlineData(OutputChoice.Aiff, "pcm_s24be", ".aiff")]
    [InlineData(OutputChoice.AacM4a, "aac", ".m4a")]
    [InlineData(OutputChoice.Mp3, "libmp3lame", ".mp3")]
    [InlineData(OutputChoice.Opus, "libopus", ".opus")]
    [InlineData(OutputChoice.OggVorbis, "libvorbis", ".ogg")]
    public void Every_audio_choice_maps_to_its_codec_and_extension(OutputChoice output, string codec, string extension)
    {
        Assert.Equal(codec, After(Args(Audio(output)), "-c:a"));
        Assert.Equal(extension, FfmpegArgs.Extension(output));
    }

    [Theory]
    [InlineData(OutputChoice.Mp4, ".mp4")]
    [InlineData(OutputChoice.Mkv, ".mkv")]
    public void Changing_a_video_container_copies_every_stream(OutputChoice output, string extension)
    {
        var args = Args(Audio(output));
        Assert.Equal("copy", After(args, "-c"));
        Assert.DoesNotContain("-c:v", args);
        Assert.Equal(extension, FfmpegArgs.Extension(output));
    }

    // M4A chapters come with a data stream that MKV can't hold, and copying it produced an unreadable file.
    [Theory]
    [InlineData(OutputChoice.Mp4)]
    [InlineData(OutputChoice.Mkv)]
    public void Copying_streams_leaves_data_streams_behind(OutputChoice output) =>
        Assert.Contains("-dn", Args(Audio(output)));

    [Theory]
    [InlineData(OutputChoice.AacM4a, "-b:a", "256k")]
    [InlineData(OutputChoice.Mp3, "-q:a", "0")]
    [InlineData(OutputChoice.Opus, "-b:a", "192k")]
    [InlineData(OutputChoice.OggVorbis, "-q:a", "8")]
    public void Lossy_outputs_use_high_quality_settings(OutputChoice output, string flag, string value) =>
        Assert.Equal(value, After(Args(Audio(output)), flag));

    [Fact]
    public void Levelling_off_by_default()
    {
        var args = Args(Audio(OutputChoice.Flac));
        Assert.DoesNotContain("-af", args);
        Assert.DoesNotContain(args, a => a.Contains("loudnorm"));
    }

    [Fact]
    public void Levelling_applies_only_when_asked()
    {
        var args = Args(Audio(OutputChoice.Flac) with { LevelLoudness = true });
        Assert.Contains("loudnorm", After(args, "-af"));
    }

    [Fact]
    public void Levelling_cannot_be_combined_with_copying_streams()
    {
        var args = Args(Audio(OutputChoice.Mp4) with { LevelLoudness = true });
        Assert.DoesNotContain("-af", args);
    }

    [Theory]
    [InlineData("h264_nvenc")]
    [InlineData("libx264")]
    public void Device_presets_use_the_tested_encoder_with_aac_and_a_height_limit(string encoder)
    {
        var args = Args(new ConvertRequest(@"D:\M\v.mkv", @"D:\M", OutputChoice.Mp4) { Reencode = true, MaxHeight = 720 }, encoder: encoder);
        Assert.Equal(encoder, After(args, "-c:v"));
        Assert.Equal("aac", After(args, "-c:a"));
        Assert.Contains("720", After(args, "-vf"));
        Assert.Equal("yuv420p", After(args, "-pix_fmt"));
        Assert.Equal("+faststart", After(args, "-movflags"));
    }

    [Fact]
    public void Software_encoding_uses_constant_quality() =>
        Assert.Equal("20", After(Args(new ConvertRequest("a", "b", OutputChoice.Mp4) { Reencode = true }, encoder: "libx264"), "-crf"));

    [Fact]
    public void Hardware_encoding_uses_a_bitrate() =>
        Assert.Equal("8M", After(Args(new ConvertRequest("a", "b", OutputChoice.Mp4) { Reencode = true }, encoder: "h264_nvenc"), "-b:v"));

    [Fact]
    public void An_existing_file_is_never_overwritten()
    {
        var args = Args(Audio(OutputChoice.Flac));
        Assert.Contains("-n", args);
        Assert.DoesNotContain("-y", args);
    }

    [Fact]
    public void Input_and_output_are_where_they_belong()
    {
        var args = Args(Audio(OutputChoice.Flac), target: @"D:\M\out.flac");
        Assert.Equal(@"D:\M\song.webm", After(args, "-i"));
        Assert.Equal(@"D:\M\out.flac", args[^1]);
    }

    [Fact]
    public void Progress_prints_as_readable_lines() => Assert.Equal("pipe:1", After(Args(Audio(OutputChoice.Flac)), "-progress"));

    [Theory]
    [InlineData(true, "0")]
    [InlineData(false, "-1")]
    public void Tags_have_their_own_switch(bool on, string value) =>
        Assert.Equal(value, After(Args(Audio(OutputChoice.Flac) with { Metadata = new MetadataOptions(Tags: on, CoverArt: false, Chapters: false) }), "-map_metadata"));

    [Theory]
    [InlineData(true, "0")]
    [InlineData(false, "-1")]
    public void Chapters_have_their_own_switch(bool on, string value) =>
        Assert.Equal(value, After(Args(Audio(OutputChoice.Flac) with { Metadata = new MetadataOptions(Tags: false, CoverArt: false, Chapters: on) }), "-map_chapters"));

    [Theory]
    [InlineData(OutputChoice.Flac)]
    [InlineData(OutputChoice.Mp3)]
    [InlineData(OutputChoice.AacM4a)]
    [InlineData(OutputChoice.Alac)]
    public void Cover_art_is_kept_where_the_format_can_hold_it(OutputChoice output)
    {
        var args = Args(Audio(output) with { Metadata = new MetadataOptions(false, CoverArt: true, false), InputHasCoverArt = true });
        Assert.DoesNotContain("-vn", args);
        Assert.Equal("attached_pic", After(args, "-disposition:v"));
        Assert.Equal("copy", After(args, "-c:v"));
    }

    [Theory]
    [InlineData(OutputChoice.Wav)]
    [InlineData(OutputChoice.Aiff)]
    [InlineData(OutputChoice.Opus)]
    [InlineData(OutputChoice.OggVorbis)]
    public void Formats_that_cannot_hold_cover_art_drop_it(OutputChoice output) =>
        Assert.Contains("-vn", Args(Audio(output) with { Metadata = new MetadataOptions(false, CoverArt: true, false), InputHasCoverArt = true }));

    // In a video file, the first video stream is the movie. Copying it would put a film in a FLAC.
    [Fact]
    public void A_video_stream_is_never_mistaken_for_cover_art() =>
        Assert.Contains("-vn", Args(Audio(OutputChoice.Flac) with { Metadata = new MetadataOptions(false, CoverArt: true, false) }));

    [Fact]
    public void Audio_output_without_cover_art_drops_the_picture_stream() =>
        Assert.Contains("-vn", Args(Audio(OutputChoice.Flac)));

    [Fact]
    public void Download_metadata_has_its_own_switches()
    {
        var none = YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", "D:", DownloadMode.Best, null));
        Assert.DoesNotContain("--embed-metadata", none);
        Assert.DoesNotContain("--embed-thumbnail", none);
        Assert.DoesNotContain("--embed-chapters", none);

        var tags = YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", "D:", DownloadMode.Best, null) { Metadata = new MetadataOptions(true, false, false) });
        Assert.Contains("--embed-metadata", tags);
        Assert.DoesNotContain("--embed-thumbnail", tags);

        var cover = YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", "D:", DownloadMode.Best, null) { Metadata = new MetadataOptions(false, true, false) });
        Assert.Contains("--embed-thumbnail", cover);
        Assert.DoesNotContain("--embed-chapters", cover);

        var chapters = YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", "D:", DownloadMode.Best, null) { Metadata = new MetadataOptions(false, false, true) });
        Assert.Contains("--embed-chapters", chapters);
        Assert.DoesNotContain("--embed-metadata", chapters);
    }
}

public sealed class FfmpegRunnerTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-ff").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    string Input()
    {
        string path = Path.Combine(_root, "song.webm");
        File.WriteAllText(path, "original bytes");
        return path;
    }

    /// <summary>Acts like ffmpeg: writes to the path it's given last, or doesn't, and exits.</summary>
    sealed class FakeFfmpeg(int exit = 0, string? output = null, Action<string>? beforeExit = null) : IProcessRunner
    {
        public string? Target;
        public IReadOnlyList<string>? Args;

        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            Args = args;
            Target = args[^1];
            File.WriteAllText(Target, "converted");
            if (output is not null) onLine(output);
            beforeExit?.Invoke(Target);
            return Task.FromResult(exit);
        }
    }

    FfmpegRunner Runner(IProcessRunner process) => new(process, "ffmpeg.exe");

    ConvertRequest Flac(string input) => new(input, _root, OutputChoice.Flac);

    [Fact]
    public async Task Conversion_keeps_original_file()
    {
        string input = Input();
        var result = await Runner(new FakeFfmpeg()).ConvertAsync(Flac(input));
        Assert.True(result.IsSuccess);
        Assert.Equal("original bytes", File.ReadAllText(input));
        Assert.Equal(Path.Combine(_root, "song.flac"), result.OutputPath);
        Assert.Equal("converted", File.ReadAllText(result.OutputPath!));
    }

    [Fact]
    public async Task Missing_output_folder_is_OutputFailure()
    {
        bool started = false;
        var process = new FakeFfmpeg(beforeExit: _ => started = true);
        var result = await Runner(process).ConvertAsync(new ConvertRequest(Input(), @"Q:\pulled\usb", OutputChoice.Flac));
        Assert.Equal(ErrorKind.OutputFailure, result.Error);
        Assert.False(started);
    }

    [Fact]
    public async Task An_output_folder_that_does_not_exist_yet_is_created()
    {
        string folder = Path.Combine(_root, "new", "folder");
        var result = await Runner(new FakeFfmpeg()).ConvertAsync(new ConvertRequest(Input(), folder, OutputChoice.Flac));
        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(folder, "song.flac"), result.OutputPath);
    }

    [Fact]
    public async Task A_missing_input_is_a_tool_failure_not_a_crash()
    {
        var result = await Runner(new FakeFfmpeg()).ConvertAsync(Flac(Path.Combine(_root, "gone.webm")));
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.ToolFailure, result.Error);
    }

    [Fact]
    public async Task An_existing_target_gets_a_new_name()
    {
        string input = Input();
        File.WriteAllText(Path.Combine(_root, "song.flac"), "earlier conversion");
        var result = await Runner(new FakeFfmpeg()).ConvertAsync(Flac(input));
        Assert.Equal(Path.Combine(_root, "song (2).flac"), result.OutputPath);
        Assert.Equal("earlier conversion", File.ReadAllText(Path.Combine(_root, "song.flac")));
    }

    [Fact]
    public async Task A_failed_conversion_leaves_no_broken_file()
    {
        var process = new FakeFfmpeg(exit: 1, output: "No space left on device");
        var result = await Runner(process).ConvertAsync(Flac(Input()));
        Assert.Equal(ErrorKind.OutputFailure, result.Error);
        Assert.Null(result.OutputPath);
        Assert.Equal(["song.webm"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_canceled_conversion_leaves_no_broken_file()
    {
        using var cancel = new CancellationTokenSource();
        var process = new FakeFfmpeg(beforeExit: _ => { cancel.Cancel(); cancel.Token.ThrowIfCancellationRequested(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(process).ConvertAsync(Flac(Input()), null, cancel.Token));
        Assert.Equal(["song.webm"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task While_running_the_output_has_a_temporary_name()
    {
        var process = new FakeFfmpeg();
        await Runner(process).ConvertAsync(Flac(Input()));
        Assert.Contains(".partial", process.Target);
        Assert.EndsWith(".flac", process.Target);
        Assert.True(Path.GetFileName(process.Target)!.Length < 64, "fixed and short, whatever the title");
    }

    [Fact]
    public async Task Progress_comes_from_time_against_duration()
    {
        var seen = new List<double>();
        var process = new FakeFfmpeg(output: "out_time_ms=30000000");
        await Runner(process).ConvertAsync(Flac(Input()) with { DurationSeconds = 60 }, seen.Add);
        Assert.Equal([50.0], seen);
    }

    [Fact]
    public async Task No_known_duration_means_no_invented_percentage()
    {
        var seen = new List<double>();
        var process = new FakeFfmpeg(output: "out_time_ms=30000000");
        await Runner(process).ConvertAsync(Flac(Input()), seen.Add);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Progress_never_passes_one_hundred()
    {
        var seen = new List<double>();
        var process = new FakeFfmpeg(output: "out_time_ms=90000000");
        await Runner(process).ConvertAsync(Flac(Input()) with { DurationSeconds = 60 }, seen.Add);
        Assert.Equal([100.0], seen);
    }

    [Fact]
    public async Task Reencoding_asks_the_probe_for_the_encoder()
    {
        var process = new FakeFfmpeg();
        var runner = new FfmpegRunner(process, "ffmpeg.exe", () => Task.FromResult("h264_qsv"));
        await runner.ConvertAsync(new ConvertRequest(Input(), _root, OutputChoice.Mp4) { Reencode = true });
        Assert.Contains("h264_qsv", process.Args!);
    }
}

public class EncoderProbeTests
{
    sealed class FakeEncoders(params string[] working) : IProcessRunner
    {
        public readonly List<string> Tried = new();

        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
        {
            string encoder = args[args.ToList().IndexOf("-c:v") + 1];
            Tried.Add(encoder);
            return Task.FromResult(working.Contains(encoder) ? 0 : 1);
        }
    }

    [Fact]
    public async Task Encoder_probe_falls_back_to_software() =>
        Assert.Equal("libx264", await new EncoderProbe(new FakeEncoders(), "ffmpeg.exe").BestH264Async());

    [Theory]
    [InlineData("h264_nvenc")]
    [InlineData("h264_qsv")]
    [InlineData("h264_amf")]
    public async Task A_hardware_encoder_counts_only_after_a_real_test_encode(string encoder) =>
        Assert.Equal(encoder, await new EncoderProbe(new FakeEncoders(encoder), "ffmpeg.exe").BestH264Async());

    [Fact]
    public async Task The_answer_is_remembered()
    {
        var process = new FakeEncoders("h264_qsv");
        var probe = new EncoderProbe(process, "ffmpeg.exe");
        await probe.BestH264Async();
        int tried = process.Tried.Count;
        await probe.BestH264Async();
        Assert.Equal(tried, process.Tried.Count);
    }

    sealed class Exploding : IProcessRunner
    {
        public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel) =>
            throw new System.ComponentModel.Win32Exception("driver crash");
    }

    [Fact]
    public async Task A_crashing_encoder_counts_as_not_working() =>
        Assert.Equal("libx264", await new EncoderProbe(new Exploding(), "ffmpeg.exe").BestH264Async());
}
