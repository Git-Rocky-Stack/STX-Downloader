using System.Diagnostics;
using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>Phase 5: which way each file's sound goes, and what the readout says.</summary>
public class BitPerfectTests
{
    static PcmSource Flac(int bits, int rate) => new("flac", bits, rate, 2, false, 60);

    [Fact]
    public void Off_means_shared_for_everything()
    {
        Assert.Equal(AudioRoute.Shared, BitPerfect.Route(false, Flac(24, 96000)));
        Assert.Equal(AudioRoute.Shared, BitPerfect.Route(false, new PcmSource("ac3", 0, 48000, 6, true, 7200)));
    }

    [Theory]
    [InlineData("flac", 24, 96000, false, AudioRoute.Exclusive)]
    [InlineData("flac", 16, 44100, false, AudioRoute.Exclusive)]
    [InlineData("alac", 24, 192000, false, AudioRoute.Exclusive)]
    [InlineData("pcm_s24le", 24, 48000, false, AudioRoute.Exclusive)]
    [InlineData("wavpack", 16, 44100, false, AudioRoute.Exclusive)]
    [InlineData("mp3", 0, 44100, false, AudioRoute.Shared)]
    [InlineData("aac", 0, 48000, false, AudioRoute.Shared)]
    [InlineData("ac3", 0, 48000, true, AudioRoute.Passthrough)]
    [InlineData("eac3", 0, 48000, true, AudioRoute.Passthrough)]
    [InlineData("dts", 0, 48000, true, AudioRoute.Passthrough)]
    [InlineData("truehd", 24, 48000, true, AudioRoute.Passthrough)]
    [InlineData("aac", 0, 48000, true, AudioRoute.Shared)]
    [InlineData("pcm_s24le", 24, 48000, true, AudioRoute.Shared)]
    public void Each_file_goes_the_way_it_can_go_unaltered(string codec, int bits, int rate, bool video, AudioRoute route) =>
        Assert.Equal(route, BitPerfect.Route(true, new PcmSource(codec, bits, rate, 2, video, 60)));

    // A wider container pads with zeros and changes no sample; a narrower one would cut bits, so it's never offered.
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void No_candidate_layout_cuts_a_bit(int bits)
    {
        var candidates = BitPerfect.Candidates(bits);
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.True(c.Valid >= bits && c.Container >= c.Valid, $"{c} for {bits}-bit"));
    }

    [Fact]
    public void Twenty_four_bits_go_in_thirty_two_first()
    {
        Assert.Equal(new DeviceFormat(32, 24, "pcm_s32le", "s32le"), BitPerfect.Candidates(24)[0]);
        Assert.Equal(new DeviceFormat(16, 16, "pcm_s16le", "s16le"), BitPerfect.Candidates(16)[0]);
    }

    [Fact]
    public void Ffmpeg_is_never_asked_to_resample_remix_or_filter()
    {
        var args = BitPerfect.FfmpegArgs(@"D:\Music\a.flac", BitPerfect.Candidates(24)[0], TimeSpan.FromSeconds(83.25));
        foreach (string forbidden in new[] { "-ar", "-ac", "-af", "-filter:a", "-sample_fmt", "-dither_method" })
            Assert.DoesNotContain(forbidden, args);
        Assert.Equal(["-ss", "83.250", "-i", @"D:\Music\a.flac"], args.Skip(4).Take(4));
        Assert.Equal(["-c:a", "pcm_s32le", "-f", "s32le", "-"], args.TakeLast(5));
    }

    [Theory]
    [InlineData(24, 96000, "24/96 BIT-PERFECT")]
    [InlineData(16, 44100, "16/44.1 BIT-PERFECT")]
    [InlineData(24, 192000, "24/192 BIT-PERFECT")]
    [InlineData(16, 48000, "16/48 BIT-PERFECT")]
    public void The_readout_says_what_reaches_the_device(int bits, int rate, string text) =>
        Assert.Equal(text, BitPerfect.Readout(AudioRoute.Exclusive, bits, rate));

    [Fact]
    public void Shared_always_says_why()
    {
        Assert.Contains("LOSSY", BitPerfect.WhyShared(new PcmSource("mp3", 0, 44100, 2, false, 60)));
        Assert.Contains("FILM", BitPerfect.WhyShared(new PcmSource("aac", 0, 48000, 2, true, 60)));
        Assert.Equal("SHARED WITH WINDOWS", BitPerfect.Readout(AudioRoute.Shared, 0, 0));
    }

    [Fact]
    public void A_probe_gives_the_files_own_depth_and_rate()
    {
        const string json = """
            {"streams":[{"codec_name":"mjpeg","codec_type":"video","disposition":{"attached_pic":1}},
                        {"codec_name":"flac","codec_type":"audio","sample_fmt":"s32","sample_rate":"96000","channels":2,"bits_per_raw_sample":"24"}],
             "format":{"duration":"212.500000"}}
            """;
        var source = PcmSource.FromProbe(json)!;
        Assert.Equal(new PcmSource("flac", 24, 96000, 2, false, 212.5), source);
        Assert.Equal(AudioRoute.Exclusive, BitPerfect.Route(true, source));
    }
}

/// <summary>
/// Phase 5, done when: a 24/96 and a 16/44.1 FLAC each reach the device unaltered. These prove the decoding leg with
/// the real FFmpeg: random samples go into a FLAC, and every sample comes back out of STX's FFmpeg command unchanged.
/// The device leg (ExclusiveRenderer) needs a sound device and is the Device-category test in Stx.Playback.Tests.
/// Needs ffmpeg on PATH; a local gate like the corpus.
/// </summary>
[Trait("Category", "Ffmpeg")]
public sealed class BitPerfectDecodeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-bitperfect").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    static byte[] Run(string tool, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(start)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Fail($"{tool} isn't on PATH. Install FFmpeg to run the Ffmpeg tests."); throw; }
        using (process)
        {
            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            string errors = process.StandardError.ReadToEnd();
            copy.Wait();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"{tool} failed: {errors}");
            return output.ToArray();
        }
    }

    // Random samples, so any rounding, dither or gain anywhere shows up.
    string MakeFlac(int bits, int rate, int frames, out int[] samples)
    {
        var random = new Random(bits * 1000 + rate);
        int channels = 2;
        samples = new int[frames * channels];
        int bytesPer = bits / 8;
        var raw = new byte[samples.Length * bytesPer];
        for (int i = 0; i < samples.Length; i++)
        {
            int value = random.Next(-(1 << (bits - 1)), 1 << (bits - 1));
            samples[i] = value;
            for (int b = 0; b < bytesPer; b++) raw[i * bytesPer + b] = (byte)(value >> (8 * b));
        }
        string rawPath = Path.Combine(_dir, $"in{bits}.raw"), flac = Path.Combine(_dir, $"{bits}-{rate}.flac");
        File.WriteAllBytes(rawPath, raw);
        Run("ffmpeg", ["-hide_banner", "-loglevel", "error", "-y", "-f", bits == 24 ? "s24le" : "s16le", "-ar", $"{rate}", "-ac", "2", "-i", rawPath, "-c:a", "flac", flac]);
        return flac;
    }

    static int[] Decode(string flac, DeviceFormat format, TimeSpan from = default)
    {
        byte[] pcm = Run("ffmpeg", BitPerfect.FfmpegArgs(flac, format, from));
        int width = format.Container / 8;
        var values = new int[pcm.Length / width];
        for (int i = 0; i < values.Length; i++)
            values[i] = width switch
            {
                2 => BitConverter.ToInt16(pcm, i * 2),
                3 => (pcm[i * 3] | pcm[i * 3 + 1] << 8 | (sbyte)pcm[i * 3 + 2] << 16),
                _ => BitConverter.ToInt32(pcm, i * 4),
            };
        return values;
    }

    [Theory]
    [InlineData(24, 96000)]
    [InlineData(16, 44100)]
    public void Every_sample_comes_out_unaltered_in_every_layout_offered(int bits, int rate)
    {
        string flac = MakeFlac(bits, rate, rate / 2, out int[] original);
        foreach (var format in BitPerfect.Candidates(bits))
        {
            int[] decoded = Decode(flac, format);
            Assert.Equal(original.Length, decoded.Length);
            // Left-justified in the container: the sample's own bits on top, zeros below.
            int shift = format.Container - bits;
            for (int i = 0; i < original.Length; i++)
                if (decoded[i] != original[i] << shift) Assert.Fail($"{format}: sample {i} is {decoded[i]}, expected {original[i] << shift}");
        }
    }

    [Fact]
    public void The_probe_reads_the_files_own_format()
    {
        string flac = MakeFlac(24, 96000, 9600, out _);
        string json = System.Text.Encoding.UTF8.GetString(Run("ffprobe", ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", flac]));
        var source = PcmSource.FromProbe(json)!;
        Assert.Equal(("flac", 24, 96000, 2), (source.Codec, source.Bits, source.Rate, source.Channels));
    }

    // A seek restarts FFmpeg part way in; the samples from there on are still the file's own, from the right place.
    [Fact]
    public void A_seek_lands_on_the_exact_sample()
    {
        string flac = MakeFlac(24, 96000, 96000, out int[] original);
        int[] decoded = Decode(flac, BitPerfect.Candidates(24)[0], TimeSpan.FromSeconds(0.25));
        int start = 24000 * 2;
        Assert.Equal(original.Length - start, decoded.Length);
        for (int i = 0; i < 1000; i++) Assert.Equal(original[start + i] << 8, decoded[i]);
    }
}
