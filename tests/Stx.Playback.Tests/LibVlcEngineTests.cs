using Stx.Core.Playback;

namespace Stx.Playback.Tests;

public class LibVlcEngineTests
{
    static readonly TimeSpan Five = TimeSpan.FromSeconds(5);

    [Fact]
    public void Plays_a_tiny_file()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.5)),
            $"state {engine.State}, position {engine.Position}, error {engine.Error}");
        Assert.True(engine.HasVideo);
        Assert.Equal(2, engine.Length.TotalSeconds, 0.2);
        Assert.True(Fixture.Within(Five, () => engine.Decoded.Pictures > 0 && engine.Decoded.AudioBlocks > 0), $"decoded {engine.Decoded}");
    }

    // Phase 4, done when: an encrypted sample is refused with no playback attempted. The disc is built from sectors
    // carrying CSS's scrambling bits, so nothing encrypted is in the repo, and LibVLC is never given it.
    [Fact]
    public void An_encrypted_disc_never_reaches_libvlc()
    {
        string disc = Directory.CreateTempSubdirectory("stx-css").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(disc, "VIDEO_TS"));
            var sector = new byte[2048];
            new byte[] { 0, 0, 1, 0xBA, 0x44, 0, 4, 0, 4, 1, 1, 0x89, 0xC3, 0xF8, 0, 0, 1, 0xE0, 0x07, 0xEC, 0x90, 0x80, 0x05 }.CopyTo(sector, 0);
            File.WriteAllBytes(Path.Combine(disc, "VIDEO_TS", "VTS_01_1.VOB"), [.. Enumerable.Repeat(sector, 16).SelectMany(b => b)]);

            using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
            engine.Open(disc);
            Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Failed), $"state {engine.State}");
            Assert.Equal(DiscCheck.Refused, engine.Error);
            Assert.Null(engine.HandedToLibVlc);
        }
        finally { Directory.Delete(disc, true); }
    }

    [Fact]
    public void A_stream_that_needs_a_listening_port_never_reaches_libvlc()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open("udp://@239.0.0.1:1234");
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Failed), $"state {engine.State}");
        Assert.Equal(StreamLink.PortRefused, engine.Error);
        Assert.Null(engine.HandedToLibVlc);
    }

    [Fact]
    public void Reports_audio_only()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny-audio.flac"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.5)),
            $"state {engine.State}, error {engine.Error}");
        Assert.False(engine.HasVideo);
    }

    [Fact]
    public void Truncated_file_ends_in_failed_state()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("truncated.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State is PlaybackState.Failed or PlaybackState.Ended), $"state {engine.State}");
        if (engine.State == PlaybackState.Failed) Assert.NotEmpty(engine.LoggedErrors);
    }

    [Fact]
    public void Opens_a_unicode_path()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("Фильм тест.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.5)),
            $"state {engine.State}, error {engine.Error}");
    }

    [Fact]
    public void Dispose_releases_the_player()
    {
        var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing));
        engine.Dispose();
        Assert.Equal(PlaybackState.Idle, engine.State);
        Assert.Null(engine.Player);
        engine.Dispose();
    }

    [Fact]
    public void Dispose_after_the_file_ends_is_safe()
    {
        var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny.mp4"));
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(8), () => engine.State == PlaybackState.Ended), $"state {engine.State}");
        Thread.Sleep(500);
        engine.Dispose();
        Assert.Equal(PlaybackState.Idle, engine.State);
    }

    // VLC 3.0.24's engine crashes the whole process on Dirac / VC-2 (the official desktop build too), so STX refuses it.
    [Fact]
    public void Dirac_is_refused_without_a_crash()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny-dirac.mkv"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Failed), $"state {engine.State}");
        Assert.Equal(LibVlcEngine.DiracRefused, engine.Error);
        Assert.Equal(0, engine.Decoded.Pictures);
    }

    // A window hidden in the tray stops decoding pictures; the sound carries on (Review Focus 5).
    [Fact]
    public void Hidden_video_stops_decoding_pictures_but_not_sound()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("moving.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.Decoded.Pictures > 10), "no pictures");
        engine.HideVideo();
        Thread.Sleep(800);
        var hidden = engine.Decoded;
        Thread.Sleep(1500);
        Assert.Equal(hidden.Pictures, engine.Decoded.Pictures);
        Assert.True(engine.Decoded.AudioBlocks > hidden.AudioBlocks, "the sound stopped too");
        engine.ShowVideo();
        Assert.True(Fixture.Within(Five, () => engine.Decoded.Pictures > hidden.Pictures), "pictures didn't come back");
    }

    [Fact]
    public void The_hardware_probe_counts_this_pc_s_cores()
    {
        var facts = HardwareProbe.Detect();
        Assert.Equal(Environment.ProcessorCount, facts.LogicalCores);
        Assert.False(facts.GpuSuperResolution);
    }

    // Closing STX just after opening a file: the engine must not free LibVLC while the file's tracks are still being read.
    [Fact]
    public void Dispose_right_after_open_is_safe()
    {
        for (int i = 0; i < 40; i++)
        {
            var engine = new LibVlcEngine(Fixture.Profile, headless: true);
            engine.Open(Fixture.File(i % 2 == 0 ? "moving.mp4" : "tiny-audio.flac"));
            Thread.Sleep(i % 8);
            engine.Dispose();
            Assert.Equal(PlaybackState.Idle, engine.State);
        }
    }

    [Fact]
    // LibVLC 3.0.24's start-paused stops before the first picture is decoded; the gallery steps one frame from there.
    public void Start_paused_opens_and_waits_at_the_start()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true) { StartPaused = true };
        engine.Open(Fixture.File("moving.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Paused), $"state {engine.State}");
        Thread.Sleep(1000);
        Assert.Equal(PlaybackState.Paused, engine.State);
        Assert.True(engine.Position < TimeSpan.FromMilliseconds(100), $"position {engine.Position}");
    }

    [Fact]
    public void Open_without_a_surface_waits_for_one()
    {
        using var engine = new LibVlcEngine(Fixture.Profile);
        engine.Open(Fixture.File("tiny.mp4"));
        Thread.Sleep(1000);
        Assert.Equal(PlaybackState.Opening, engine.State);
        Assert.Null(engine.Player);
    }

    [Fact]
    public void Changed_fires_as_playback_starts()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        int changes = 0;
        engine.Changed += (_, _) => Interlocked.Increment(ref changes);
        engine.Open(Fixture.File("tiny.mp4"));
        Assert.True(Fixture.Within(Five, () => Volatile.Read(ref changes) > 0 && engine.State == PlaybackState.Playing));
    }

    // LibVLC 3.0.24 still logs Windows' one refusal of a mono stream on the first try; what matters is where the sound ends up.
    [Fact, Trait("Category", "Device")]
    public void Mono_audio_plays_through_wasapi_in_stereo()
    {
        foreach (var name in new[] { "tiny.mp4", "tiny-audio.flac" })
        {
            using var engine = new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true);
            engine.Open(Fixture.File(name));
            Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.5)), $"{name}: state {engine.State}");
            Assert.True(Fixture.Within(Five, () => engine.AudioStream == "wasapi" && engine.AudioOutput.Contains("Stereo", StringComparison.Ordinal)),
                $"{name}: stream '{engine.AudioStream}', output '{engine.AudioOutput}'");
            Assert.True(Fixture.Within(Five, () => engine.Player is { Mute: false }), $"{name}: still muted");
            Assert.True(engine.LoggedErrors.Count(e => e.Contains("cannot negotiate audio format", StringComparison.Ordinal)) <= 1, string.Join(" | ", engine.LoggedErrors));
        }
    }

    [Fact, Trait("Category", "Device")]
    public void Surround_is_not_downmixed()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true);
        engine.Open(Fixture.File("surround-5.1.flac"));
        Assert.True(Fixture.Within(Five, () => engine.AudioOutput.Length > 0), "no audio output logged");
        Assert.Contains("3F2R/LFE", engine.AudioOutput);
        Assert.Equal("wasapi", engine.AudioStream);
    }
}
