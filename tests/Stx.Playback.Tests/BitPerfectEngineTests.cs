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

    // Independent review, finding 2: anything thrown while deciding the route used to vanish into the task, leaving the
    // player on OPENING for good. A probe STX can't read still plays, shared, and the readout says why.
    [Fact]
    public void A_probe_that_cant_be_read_still_plays_shared_and_says_why()
    {
        string dir = Directory.CreateTempSubdirectory("stx-probe").FullName;
        try
        {
            // A channel count of 2.5 is a JSON number that isn't an int: reading it throws.
            string probe = Path.Combine(dir, "ffprobe.cmd");
            File.WriteAllText(probe, "@echo {\"streams\":[{\"codec_type\":\"audio\",\"codec_name\":\"flac\",\"sample_rate\":\"44100\",\"channels\":2.5}]}\r\n");
            using var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true), () => true, () => probe, () => probe);
            engine.Open(Fixture.File("tiny-audio.flac"));
            Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing), $"state {engine.State}");
            Assert.Equal("SHARED: STX COULDN'T READ THE FILE'S SOUND", engine.AudioReadout);
        }
        finally { Directory.Delete(dir, true); }
    }

    // Independent review, finding 2: the 10 second probe limit didn't hold, because the output was read to its end
    // before the wait began. An ffprobe that hangs (a stalled network share) is given up on, and the file plays shared.
    [Fact]
    public void A_hung_probe_is_given_up_on_within_its_limit()
    {
        string dir = Directory.CreateTempSubdirectory("stx-probe").FullName;
        try
        {
            string probe = Path.Combine(dir, "ffprobe.cmd");
            File.WriteAllText(probe, "@ping -n 40 127.0.0.1 >nul\r\n");
            using var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true), () => true, () => probe, () => probe);
            engine.Open(Fixture.File("tiny-audio.flac"));
            Assert.True(Fixture.Within(TimeSpan.FromSeconds(20), () => engine.State == PlaybackState.Playing), $"state {engine.State}");
        }
        // A probe that wasn't killed may still hold its script; the temp folder is left for Windows then.
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}

/// <summary>
/// Phase 5, done when: a 24/96 and a 16/44.1 FLAC each reach the device unaltered, and shared mode still works. These
/// need a real sound device and FFmpeg on PATH, so they're a local gate. The readout is what was actually opened on the
/// device. A loopback bit comparison needs hardware loopback (an S/PDIF out wired to an in); see the Phase 2-7 report.
/// </summary>
[Trait("Category", "Device"), Collection(SoundDeviceCollection.Name)]
public sealed class BitPerfectDeviceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-exclusive").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    static string Tool(string name) =>
        Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator).Select(d => Path.Combine(d, name + ".exe")).FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException(name + " isn't on PATH.");

    string Flac(int bits, int rate, int seconds = 4)
    {
        string flac = Path.Combine(_dir, $"{bits}-{rate}-{seconds}.flac");
        var start = new ProcessStartInfo(Tool("ffmpeg")) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate={rate}:duration={seconds}", "-ac", "2", "-sample_fmt", bits == 24 ? "s32" : "s16", "-bits_per_raw_sample", $"{bits}", "-c:a", "flac", flac })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return flac;
    }

    // 44.1 and 96 need a device that takes them exclusively (a USB DAC does). The reference PC's Realtek takes 48 kHz
    // only (16 and 24-in-32; probed 2026-11-05), so the 48 kHz rows are the ones that hold the device there.
    [Theory]
    [InlineData(24, 96000, "24/96 BIT-PERFECT")]
    [InlineData(16, 44100, "16/44.1 BIT-PERFECT")]
    [InlineData(24, 48000, "24/48 BIT-PERFECT")]
    [InlineData(16, 48000, "16/48 BIT-PERFECT")]
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

    RoutingEngine Held(string flac)
    {
        var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true), () => true, () => Tool("ffmpeg"), () => Tool("ffprobe"));
        engine.Open(flac);
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => engine.State == PlaybackState.Playing), $"state {engine.State}, error {engine.Error}");
        Assert.EndsWith("BIT-PERFECT", engine.AudioReadout);
        return engine;
    }

    // A held track reaches ENDED at its end, and the device is let go then.
    [Fact]
    public void A_held_track_ends_at_its_end()
    {
        using var engine = Held(Flac(16, 48000, seconds: 2));
        var clock = Stopwatch.StartNew();
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(6), () => engine.State == PlaybackState.Ended), $"state {engine.State} after {clock.Elapsed}");
        Assert.Equal("", engine.AudioReadout);
    }

    // Independent review, finding 10. MusicEngine's end-of-track handler takes its lock; Stop, a seek or the next track
    // takes the same lock and then waits for the renderer's thread. When the end was announced on that thread, a Stop
    // landing as a track ended waited out the whole 3 second join. This sets that race up on purpose: the handler is
    // running and wants the lock, and the lock's holder lets the device go.
    [Fact]
    public void Letting_go_as_a_track_ends_does_not_wait_on_the_end_handler()
    {
        // Disposed once, inside the lock, where the race is.
        var renderer = ExclusiveRenderer.Open(48000, 2, BitPerfect.Candidates(16));
        var gate = new object();
        using var entered = new ManualResetEventSlim();
        renderer.Finished += (_, _) => { entered.Set(); lock (gate) { } };
        TimeSpan took;
        lock (gate)
        {
            renderer.Play(new MemoryStream(new byte[48000 * 4 / 4])); // a quarter second of silence, 16-bit stereo
            bool announced = entered.Wait(TimeSpan.FromSeconds(5));
            var clock = Stopwatch.StartNew();
            renderer.Dispose();
            took = clock.Elapsed;
            Assert.True(announced, "the end was never announced");
        }
        Assert.True(took < TimeSpan.FromSeconds(1), $"letting the device go took {took}");
    }

    // Independent review, finding 10: a seek killed the decoder while the renderer was still reading from it. The
    // closed pipe read as the end of the file, so seeking could end playback. After many seeks it must still be playing.
    [Fact]
    public void Seeking_while_held_keeps_playing_from_the_new_place()
    {
        using var engine = Held(Flac(16, 48000, seconds: 20));
        for (int i = 0; i < 15; i++)
        {
            engine.Seek(TimeSpan.FromSeconds(1 + i % 5));
            Thread.Sleep(60);
        }
        engine.Seek(TimeSpan.FromSeconds(10));
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(3), () => engine.Position > TimeSpan.FromSeconds(10.5)), $"state {engine.State}, position {engine.Position}");
        Assert.Equal(PlaybackState.Playing, engine.State);
        Assert.EndsWith("BIT-PERFECT", engine.AudioReadout);
    }

    // Review 2026-11-07: a decoder that died early (the file deleted, a damaged share) ran the stream dry, which read as
    // the end of the track. FFmpeg's exit code tells the two apart. A file that isn't there is the quickest such death.
    [Fact]
    public void A_file_ffmpeg_cant_read_to_its_end_fails_and_says_so()
    {
        using var music = new MusicEngine(Tool("ffmpeg"));
        music.Open(Path.Combine(_dir, "gone.flac"), new PcmSource("flac", 16, 48000, 2, false, 4));
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(8), () => music.State is PlaybackState.Failed or PlaybackState.Ended), $"state {music.State}");
        Assert.Equal(PlaybackState.Failed, music.State);
        Assert.Equal(MusicEngine.DecodeFailed, music.Error);
    }

    // Review 2026-11-07: a pump stuck reading a stalled source outlived Halt's 3 second wait, and the next Play set the
    // shared stop flag back, so the old pump carried on beside the new one, writing into the same device.
    [Fact]
    public void A_pump_left_in_a_stalled_read_never_writes_again()
    {
        using var renderer = ExclusiveRenderer.Open(48000, 2, BitPerfect.Candidates(16));
        using var stalled = new StallingStream(48000 * 4 / 2, TimeSpan.FromSeconds(6)); // half a second, then 6 s of nothing
        renderer.Play(stalled);
        Assert.True(stalled.Stalled.Wait(TimeSpan.FromSeconds(5)), "the source never stalled");
        renderer.Halt();
        using var ended = new ManualResetEventSlim();
        renderer.Finished += (_, run) => { if (run == renderer.CurrentRun) ended.Set(); };
        renderer.Play(new MemoryStream(new byte[48000 * 4 / 4]));
        Assert.True(ended.Wait(TimeSpan.FromSeconds(5)), "the new run never ended");
        long written = renderer.FramesWritten;
        Thread.Sleep(TimeSpan.FromSeconds(6)); // the stalled read returns in here
        Assert.Equal(written, renderer.FramesWritten);
    }

    // Gives some bytes, then blocks once for the stall before giving more: a share that stopped answering.
    sealed class StallingStream(int before, TimeSpan stall) : Stream
    {
        int _given;
        public ManualResetEventSlim Stalled { get; } = new();
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_given >= before && !Stalled.IsSet) { Stalled.Set(); Thread.Sleep(stall); }
            int n = Math.Min(count, Stalled.IsSet ? count : before - _given);
            Array.Clear(buffer, offset, n);
            _given += n;
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _given; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) Stalled.Dispose(); base.Dispose(disposing); }
    }

    // Handoff 2026-11-05, item 4: a second Dispose signalled a wait handle the first had already disposed.
    [Fact]
    public void Disposing_the_renderer_twice_is_harmless()
    {
        var renderer = ExclusiveRenderer.Open(48000, 2, BitPerfect.Candidates(16));
        renderer.Dispose();
        renderer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => renderer.Play(Stream.Null));
    }

    // Shared mode still works, and the device goes back to Windows when STX stops.
    [Fact]
    public void Stopping_lets_the_device_go()
    {
        // 48 kHz, which nearly every device takes exclusively: at 44.1 the Realtek refused, played shared, and this
        // passed without the device ever having been held.
        string flac = Flac(16, 48000);
        using (var engine = new RoutingEngine(new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true), () => true, () => Tool("ffmpeg"), () => Tool("ffprobe")))
        {
            engine.Open(flac);
            Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => engine.State == PlaybackState.Playing));
            Assert.Equal("16/48 BIT-PERFECT", engine.AudioReadout);
            engine.Stop();
        }
        using var shared = new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true);
        shared.Open(flac);
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(5), () => shared.State == PlaybackState.Playing && shared.AudioStream == "wasapi"), $"stream {shared.AudioStream}");
    }
}
