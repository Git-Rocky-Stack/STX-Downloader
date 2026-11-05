using System.Diagnostics;
using Stx.Core.Playback;

namespace Stx.Playback.Tests;

/// <summary>Phase 5: the routing engine. Without a sound device, only the shared fallbacks can be tested; they run in CI.</summary>
public class BitPerfectRoutingTests
{
    static readonly TimeSpan Five = TimeSpan.FromSeconds(5);

    [Fact]
    public void Passthrough_is_an_instance_option_and_off_unless_asked()
    {
        Assert.Contains("--mmdevice-passthrough=2", LibVlcOptions.For(headless: false, passthrough: true));
        Assert.DoesNotContain(LibVlcOptions.For(headless: false), o => o.StartsWith("--mmdevice-passthrough", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("'spdi' 48000 Hz Stereo", true)]
    [InlineData("'eac3' 48000 Hz 3F2R/LFE", true)]
    [InlineData("'trhd' 48000 Hz 3F2M2R/LFE", true)]
    [InlineData("'f32l' 48000 Hz 3F2R/LFE", false)]
    [InlineData("", false)]
    public void A_bitstream_is_told_from_pcm_by_libvlcs_own_log(string output, bool bitstream) =>
        Assert.Equal(bitstream, LibVlcEngine.IsBitstream(output));

    [Fact]
    public void With_bit_perfect_off_everything_plays_through_vlc_and_says_nothing()
    {
        using var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true), () => false, () => null, () => null);
        engine.Open(Fixture.File("tiny-audio.flac"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing), $"state {engine.State}");
        Assert.Equal("", engine.AudioReadout);
        Assert.False(engine.VolumeFixed);
    }

    // Never fails silently: without FFmpeg yet, a lossless file plays shared and the readout says why.
    [Fact]
    public void Without_ffmpeg_it_plays_shared_and_says_why()
    {
        using var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true), () => true, () => null, () => null);
        engine.Open(Fixture.File("tiny-audio.flac"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing), $"state {engine.State}");
        Assert.Equal("SHARED: FFMPEG ISN'T DOWNLOADED YET", engine.AudioReadout);
    }
}

/// <summary>
/// Phase 5, done when: a 24/96 and a 16/44.1 FLAC each reach the device unaltered, and shared mode still works. These
/// need a real sound device and FFmpeg on PATH, so they're a local gate. The readout is what was actually opened on the
/// device. A loopback bit comparison needs hardware loopback (an S/PDIF out wired to an in); see the Phase 2-7 report.
/// </summary>
[Trait("Category", "Device")]
public sealed class BitPerfectDeviceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-exclusive").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    static string Tool(string name) =>
        Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(d => Path.Combine(d, name + ".exe")).FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException(name + " isn't on PATH.");

    string Flac(int bits, int rate)
    {
        string flac = Path.Combine(_dir, $"{bits}-{rate}.flac");
        var start = new ProcessStartInfo(Tool("ffmpeg")) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate={rate}:duration=4", "-ac", "2", "-sample_fmt", bits == 24 ? "s32" : "s16", "-bits_per_raw_sample", $"{bits}", "-c:a", "flac", flac })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return flac;
    }

    [Theory]
    [InlineData(24, 96000, "24/96 BIT-PERFECT")]
    [InlineData(16, 44100, "16/44.1 BIT-PERFECT")]
    public void A_flac_holds_the_device_at_its_own_format(int bits, int rate, string readout)
    {
        string flac = Flac(bits, rate);
        using var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true), () => true, () => Tool("ffmpeg"), () => Tool("ffprobe"));
        engine.Open(flac);
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.5)),
            $"state {engine.State}, readout {engine.AudioReadout}, error {engine.Error}");
        Assert.Equal(readout, engine.AudioReadout);
        Assert.True(engine.VolumeFixed);
        engine.Stop();
    }

    // Shared mode still works, and the device goes back to Windows when STX stops.
    [Fact]
    public void Stopping_lets_the_device_go()
    {
        string flac = Flac(16, 44100);
        using (var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true), () => true, () => Tool("ffmpeg"), () => Tool("ffprobe")))
        {
            engine.Open(flac);
            Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => engine.State == PlaybackState.Playing));
            engine.Stop();
        }
        using var shared = new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true);
        shared.Open(flac);
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => shared.State == PlaybackState.Playing && shared.AudioStream == "wasapi"), $"stream {shared.AudioStream}");
    }
}
