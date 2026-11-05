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
        if (engine.State == PlaybackState.Failed) Assert.False(string.IsNullOrWhiteSpace(engine.Error));
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
