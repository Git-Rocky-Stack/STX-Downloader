using Stx.Audio;
using Xunit;

namespace Stx.Audio.Tests;

static class Fixture
{
    public static string Dir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "fixtures", "audio");
    }

    public static string Text(string name) => File.ReadAllText(Path.Combine(Dir(), name));
    public static Stream Pcm(string name) => File.OpenRead(Path.Combine(Dir(), name));

    public static MemoryStream Silence(int seconds, int rate = 48000) => new(new byte[seconds * rate * 4]);
}

public class SpectrumTests
{
    [Fact]
    public void Rolloff_of_lowpassed_noise_at_16k_is_within_500Hz()
    {
        using var pcm = Fixture.Pcm("noise-lowpass-16k-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.NotNull(result.RollOffHz);
        Assert.InRange(result.RollOffHz!.Value, 15500, 16500);
    }

    // A real file with no cutoff must never be accused of one. Read 551 Hz before this was fixed.
    [Fact]
    public void Full_band_audio_with_a_natural_slope_reports_no_cutoff()
    {
        using var pcm = Fixture.Pcm("pink-fullband-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.NotNull(result.RollOffHz);
        Assert.True(result.RollOffHz > 23000, $"read {result.RollOffHz}");
    }

    [Fact]
    public void A_real_128k_mp3_shows_its_encoder_cutoff()
    {
        using var pcm = Fixture.Pcm("mp3-128k-real-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.InRange(result.RollOffHz!.Value, 15000, 18000);
    }

    // Made here from noise, not recorded: pink noise cut off near 5 kHz plus faint white noise, through real Opus at 256 kbps.
    // Sound that ends far below where its modern codec's own band ends, and only
    // the first of those is the truth about the source. Read 20.1 kHz before this was fixed.
    [Fact]
    public void A_codec_edge_far_below_the_sound_is_not_the_roll_off()
    {
        using var pcm = Fixture.Pcm("narrowband-opus-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.InRange(result.RollOffHz!.Value, 3000, 10000);
    }

    [Fact]
    public void Sine_at_1k_peaks_at_1k_within_one_bin()
    {
        using var pcm = Fixture.Pcm("sine-1k-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.InRange(result.PeakHz, 1000 - result.BinHz, 1000 + result.BinHz);
    }

    [Fact]
    public void Silence_returns_no_rolloff_without_throwing()
    {
        using var pcm = Fixture.Silence(1);
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.Null(result.RollOffHz);
        Assert.Equal(double.NegativeInfinity, result.PeakLeftDb);
    }

    [Fact]
    public void Empty_input_returns_no_result_without_throwing()
    {
        using var pcm = new MemoryStream();
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.Null(result.RollOffHz);
        Assert.Empty(result.Envelope);
    }

    [Fact]
    public void Input_shorter_than_one_window_still_reads_its_peak()
    {
        var samples = new float[100];
        samples[50] = 0.5f;
        var bytes = new byte[400];
        Buffer.BlockCopy(samples, 0, bytes, 0, 400);
        var result = SpectrumAnalyzer.Analyze(new MemoryStream(bytes), 48000);
        Assert.Equal(-6.02, result.PeakLeftDb, 1);
    }

    [Fact]
    public void A_cut_off_sample_at_the_end_is_ignored()
    {
        var bytes = new byte[4 * 4096 + 2];
        var result = SpectrumAnalyzer.Analyze(new MemoryStream(bytes), 48000);
        Assert.Null(result.RollOffHz);
    }

    [Fact]
    public void Peaks_are_measured_per_channel()
    {
        using var pcm = Fixture.Pcm("stereo-half-eighth-48k.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000, channels: 2);
        // FFmpeg's sine source peaks at 1/8 of full scale, and the fixture scales that by 0.5 and 0.125.
        Assert.Equal(-24.08, result.PeakLeftDb, 0);
        Assert.Equal(-36.12, result.PeakRightDb, 0);
    }

    [Fact]
    public void Mono_reports_the_same_peak_on_both_sides()
    {
        using var pcm = Fixture.Pcm("sine-1k-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.Equal(result.PeakLeftDb, result.PeakRightDb);
    }

    [Fact]
    public void Envelope_is_a_fixed_size_and_follows_the_sound()
    {
        using var pcm = Fixture.Pcm("stereo-half-eighth-48k.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000, channels: 2);
        Assert.Equal(SpectrumAnalyzer.EnvelopePoints, result.Envelope.Count);
        Assert.All(result.Envelope, p => Assert.True(p.LeftDb > p.RightDb));
    }

    [Fact]
    public void Spectrum_is_a_fixed_number_of_bands_rising_at_the_tone()
    {
        using var pcm = Fixture.Pcm("sine-1k-48k-mono.f32");
        var result = SpectrumAnalyzer.Analyze(pcm, 48000);
        Assert.Equal(SpectrumAnalyzer.Bands, result.Spectrum.Count);
        int loudest = result.Spectrum.Select((b, i) => (b.Db, i)).MaxBy(x => x.Db).i;
        Assert.InRange(result.Spectrum[loudest].Hz, 800, 1250);
    }

    [Fact]
    public void The_same_audio_always_gives_the_same_answer()
    {
        using var a = Fixture.Pcm("noise-lowpass-16k-48k-mono.f32");
        using var b = Fixture.Pcm("noise-lowpass-16k-48k-mono.f32");
        Assert.Equal(SpectrumAnalyzer.Analyze(a, 48000).Spectrum, SpectrumAnalyzer.Analyze(b, 48000).Spectrum);
    }

    [Fact]
    public void Fft_finds_a_known_tone()
    {
        const int N = 1024;
        var re = new double[N];
        var im = new double[N];
        for (int i = 0; i < N; i++) re[i] = Math.Cos(2 * Math.PI * 64 * i / N);
        Fft.Transform(re, im);
        int loudest = Enumerable.Range(0, N / 2).MaxBy(i => re[i] * re[i] + im[i] * im[i]);
        Assert.Equal(64, loudest);
    }

    [Fact]
    public void Fft_refuses_a_size_it_cannot_do() =>
        Assert.Throws<ArgumentException>(() => Fft.Transform(new double[1000], new double[1000]));
}

public class MeasurerArgsTests
{
    [Fact]
    public void Loudness_filter_reads_the_first_audio_stream_without_conflicting_options()
    {
        var args = AudioMeasurer.LoudnessArgs(@"D:\a.mp4");
        Assert.DoesNotContain("-filter_complex", args);
        Assert.Equal("ebur128=peak=true", args[args.ToList().IndexOf("-af") + 1]);
        Assert.Equal("0:a:0", args[args.ToList().IndexOf("-map") + 1]);
        Assert.Equal(@"D:\a.mp4", args[args.ToList().IndexOf("-i") + 1]);
    }
}

public class ProbeTests
{
    [Fact]
    public void Probe_parses_chevrolet_fixture()
    {
        var probe = ProbeParser.Parse(Fixture.Text("probe-chevrolet.json"))!;
        Assert.Equal("aac", probe.Codec);
        Assert.Equal(48000, probe.SampleRate);
        Assert.Equal(2, probe.Channels);
        Assert.Equal(192, probe.BitrateKbps);
        Assert.Equal(1048.128, probe.DurationSeconds);
        Assert.False(probe.HasCoverArt);
    }

    [Fact]
    public void Video_with_no_audio_returns_null_facts() =>
        Assert.Null(ProbeParser.Parse("""{"streams":[{"codec_type":"video","codec_name":"h264"}],"format":{}}"""));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"streams":"x"}""")]
    public void Unreadable_probe_output_returns_null(string json) => Assert.Null(ProbeParser.Parse(json));

    [Fact]
    public void A_stream_without_a_bitrate_uses_its_bps_tag()
    {
        var probe = ProbeParser.Parse("""{"streams":[{"codec_type":"audio","codec_name":"opus","sample_rate":"48000","channels":2,"tags":{"BPS":"128000"}}],"format":{"bit_rate":"9000000"}}""")!;
        Assert.Equal(128, probe.BitrateKbps);
    }

    [Fact]
    public void An_audio_only_file_can_use_the_format_bitrate()
    {
        var probe = ProbeParser.Parse("""{"streams":[{"codec_type":"audio","codec_name":"flac","sample_rate":"44100","channels":2}],"format":{"bit_rate":"900000"}}""")!;
        Assert.Equal(900, probe.BitrateKbps);
    }

    [Fact]
    public void A_video_file_never_passes_its_total_bitrate_off_as_audio()
    {
        var probe = ProbeParser.Parse("""{"streams":[{"codec_type":"video","codec_name":"h264"},{"codec_type":"audio","codec_name":"opus","sample_rate":"48000","channels":2}],"format":{"bit_rate":"9000000"}}""")!;
        Assert.Null(probe.BitrateKbps);
    }

    [Fact]
    public void An_attached_picture_is_cover_art_not_video()
    {
        var probe = ProbeParser.Parse("""{"streams":[{"codec_type":"audio","codec_name":"aac","sample_rate":"44100","channels":2},{"codec_type":"video","codec_name":"mjpeg","disposition":{"attached_pic":1}}],"format":{"bit_rate":"128000"}}""")!;
        Assert.True(probe.HasCoverArt);
        Assert.Equal(128, probe.BitrateKbps);
    }

    [Theory]
    [InlineData("aac", true)]
    [InlineData("opus", true)]
    [InlineData("mp3", true)]
    [InlineData("vorbis", true)]
    [InlineData("ac3", true)]
    [InlineData("eac3", true)]
    [InlineData("flac", false)]
    [InlineData("alac", false)]
    [InlineData("pcm_s24le", false)]
    [InlineData("wavpack", false)]
    [InlineData("", true)]
    public void Lossless_is_only_claimed_for_known_lossless_codecs(string codec, bool lossy) =>
        Assert.Equal(lossy, AudioFacts.IsLossyCodec(codec));
}

public class LoudnessTests
{
    [Fact]
    public void Loudness_parses_real_ebur128_output()
    {
        var loudness = LoudnessParser.Parse(Fixture.Text("ebur128-sample.txt"));
        Assert.Equal(-33.1, loudness.Lufs);
        Assert.Equal(-30.1, loudness.TruePeakDb);
    }

    [Fact]
    public void The_summary_wins_over_running_values()
    {
        const string text = "[Parsed_ebur128_0] t: 1 I: -70.0 LUFS\n  Integrated loudness:\n    I:         -14.2 LUFS\n  True peak:\n    Peak:       -1.5 dBFS\n";
        var loudness = LoudnessParser.Parse(text);
        Assert.Equal(-14.2, loudness.Lufs);
        Assert.Equal(-1.5, loudness.TruePeakDb);
    }

    [Fact]
    public void Silence_reads_as_nothing_not_a_number()
    {
        var loudness = LoudnessParser.Parse("  Integrated loudness:\n    I:         -70.0 LUFS\n  True peak:\n    Peak:      -inf dBFS\n");
        Assert.Equal(-70.0, loudness.Lufs);
        Assert.Null(loudness.TruePeakDb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error: something else")]
    public void Output_with_no_summary_is_null_not_invented(string text)
    {
        var loudness = LoudnessParser.Parse(text);
        Assert.Null(loudness.Lufs);
        Assert.Null(loudness.TruePeakDb);
    }
}

public class ReportedFactsTests
{
    static readonly YtDlpFormat Format = new("mp4a.40.2", 388, 48000, 6);

    [Fact]
    public void Reported_facts_are_labeled_reported()
    {
        var facts = ReportedFacts.From(Format);
        Assert.False(facts.MeasuredFromFile);
        Assert.Equal("Reported", facts.Label);
        Assert.Equal("mp4a.40.2", facts.Codec);
        Assert.Equal(388, facts.BitrateKbps);
        Assert.Equal(48000, facts.SampleRate);
        Assert.Equal(6, facts.Channels);
    }

    [Fact]
    public void Reported_facts_hold_no_measurements()
    {
        var facts = ReportedFacts.From(Format);
        Assert.Null(facts.RollOffHz);
        Assert.Null(facts.LoudnessLufs);
        Assert.Null(facts.TruePeakDb);
        Assert.Empty(facts.Spectrum);
        Assert.Empty(facts.Envelope);
    }

    [Fact]
    public void Measured_facts_are_labeled_measured()
    {
        var probe = new ProbeFacts("aac", 192, 48000, 2, 10, false);
        var spectrum = new SpectrumResult([], [], 16000, 1000, 11.7, -6, -7);
        var facts = AudioFacts.Measured(probe, spectrum, new Loudness(-14, -1));
        Assert.True(facts.MeasuredFromFile);
        Assert.Equal("Measured", facts.Label);
        Assert.Equal(16000, facts.RollOffHz);
        Assert.Equal(-14, facts.LoudnessLufs);
    }

    [Fact]
    public void Formats_read_from_real_shaped_json()
    {
        var formats = YtDlpFormat.ParseAll("""{"formats":[{"format_id":"251","acodec":"opus","abr":129.1,"asr":48000,"audio_channels":2},{"format_id":"396","acodec":"none","vcodec":"av01"},{"format_id":"258","acodec":"mp4a.40.2","abr":388,"asr":48000,"audio_channels":6}]}""");
        Assert.Equal(2, formats.Count);
        Assert.Equal(new YtDlpFormat("opus", 129, 48000, 2), formats[0]);
    }

    [Fact]
    public void The_best_reported_audio_is_the_highest_bitrate()
    {
        var best = YtDlpFormat.Best([new("opus", 129, 48000, 2), new("mp4a.40.2", 388, 48000, 6), new("mp4a.40.5", 195, 24000, 6)]);
        Assert.Equal(388, best!.Bitrate);
    }

    [Fact]
    public void Nothing_to_read_is_empty_not_a_crash()
    {
        Assert.Empty(YtDlpFormat.ParseAll("nonsense"));
        Assert.Null(YtDlpFormat.Best([]));
    }
}
