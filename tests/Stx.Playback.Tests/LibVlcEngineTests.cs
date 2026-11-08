using Stx.Core.Playback;

namespace Stx.Playback.Tests;

[Collection(SoundDeviceCollection.Name)]
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

    // Phase 6, done when: SHARPEN visibly applies. VLC's own log confirms its sharpen filter is on the picture; the
    // screenshot comparison of the effect is a local gate (PlayerPlaybackTests).
    [Fact]
    public void Sharpen_loads_vlcs_filter_and_a_live_change_carries_on_where_it_was()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("tiny.mp4"));
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.6)), $"state {engine.State}");
        Assert.Null(engine.EnhanceSeen);

        var before = engine.Position;
        engine.Enhance = EnhanceLevel.Sharpen;
        Assert.True(Fixture.Within(Five, () => engine.EnhanceSeen == EnhanceLevel.Sharpen), $"seen {engine.EnhanceSeen}, errors {string.Join("; ", engine.LoggedErrors)}");
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position >= before - TimeSpan.FromSeconds(0.3)),
            $"state {engine.State}, position {engine.Position}, before {before}");
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

    // Review 2026-11-07: a Dirac stream reached the decoder, since LibVLC's check reads local files only. Served from
    // this PC, it's refused with the same message, and nothing is decoded.
    [Fact]
    public void A_dirac_stream_is_refused_without_a_crash()
    {
        using var server = new LocalHttp(Fixture.File("tiny-dirac.mkv"), "video/x-matroska");
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(server.Url);
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Failed), $"state {engine.State}");
        Assert.Equal(LibVlcEngine.DiracRefused, engine.Error);
        Assert.Equal(0, engine.Decoded.Pictures);
    }

    // The check lets other streams through: the same server with an H.264 file plays it.
    [Fact]
    public void A_stream_that_isnt_dirac_plays()
    {
        using var server = new LocalHttp(Fixture.File("moving.mp4"), "video/mp4");
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(server.Url);
        Assert.True(Fixture.Within(Five, () => engine.Decoded.Pictures > 10), $"state {engine.State}, {engine.Decoded.Pictures} pictures");
        Assert.Null(engine.Error);
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
        // What this PC's adapter has, by STX's rule: false on the reference UHD 620, true on an Arc (the Core Ultra 7
        // 258V's 140V, 2026-11-07). It said False outright before, which only held on the reference PC.
        Assert.Equal(Enhance.SuperResolution(HardwareProbe.Gpu()), facts.GpuSuperResolution);
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

    // Phase 4 was built with no live stream ever played. This one is a real internet radio station, so it's a local
    // gate (Category=Network), never CI's: it needs the internet and the station on the air.
    [Fact, Trait("Category", "Network")]
    public void A_live_internet_radio_stream_plays()
    {
        const string station = "https://ice1.somafm.com/groovesalad-128-mp3";
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true, realAudio: true);
        engine.Open(station);
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(20), () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(3)),
            $"state {engine.State}, position {engine.Position}, error {engine.Error}");
        Assert.True(engine.Decoded.AudioBlocks > 0, $"decoded {engine.Decoded}");
        Assert.Equal(station, engine.HandedToLibVlc);
    }

    // Handoff 2026-11-05, item 3: a stream that failed said "THIS FILE COULDN'T BE PLAYED", or nothing. One that never
    // answers now says to check the address and the connection. A port nothing listens on, so it runs anywhere.
    [Fact]
    public void A_stream_that_cant_be_reached_says_so()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open($"http://127.0.0.1:{port}/radio.mp3");
        Assert.True(Fixture.Within(TimeSpan.FromSeconds(15), () => engine.State == PlaybackState.Failed), $"state {engine.State}");
        Assert.Equal(LibVlcEngine.StreamUnreachable, engine.Error);
    }

    // Review 2026-11-07: STOP pressed while a file's tracks were still being read didn't call the open off, so the
    // file started anyway and played under a plate that said it was stopped.
    [Fact]
    public void Stop_during_the_open_keeps_it_stopped()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(Fixture.File("moving.mp4"));
        engine.Stop();
        Thread.Sleep(2500);
        Assert.Equal(PlaybackState.Idle, engine.State);
        Assert.Equal(0, engine.Decoded.Pictures);
    }

    // Review 2026-11-07: a file: address was turned into its path for the checks, then handed to LibVLC as given, so
    // LibVLC looked for a file literally named "file:///...".
    [Fact]
    public void A_file_address_plays_the_file()
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(new Uri(Fixture.File("tiny.mp4")).AbsoluteUri);
        Assert.True(Fixture.Within(Five, () => engine.State == PlaybackState.Playing && engine.Position > TimeSpan.FromSeconds(0.3)),
            $"state {engine.State}, error {engine.Error}, handed {engine.HandedToLibVlc}");
        Assert.Equal(Fixture.File("tiny.mp4"), engine.HandedToLibVlc);
    }

    // Review finding 5: a disc folder on a network share never reached VLC's DVD reader. Reached through this PC's own
    // admin share, the reader must get as far as reading the folder's VIDEO_TS.IFO, as it does for the same folder on C:.
    // A local gate (Category=Network): it needs \\localhost\C$, which only an administrator's account can open.
    [Fact, Trait("Category", "Network")]
    public void A_disc_folder_on_a_share_reaches_vlcs_disc_reader()
    {
        if (!Directory.Exists(@"\\localhost\C$\Windows")) Assert.Skip(@"\\localhost\C$ isn't open to this account.");
        string local = Directory.CreateTempSubdirectory("stx-share-disc").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(local, "VIDEO_TS"));
            File.WriteAllBytes(Path.Combine(local, "VIDEO_TS", "VIDEO_TS.IFO"), new byte[4096]);
            string share = @"\\localhost\" + local[0] + "$" + local[2..];
            LibVLCSharp.Shared.Core.Initialize();
            var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var vlc = new LibVLCSharp.Shared.LibVLC(enableDebugLogs: true, "--aout=dummy", "--vout=dummy");
            vlc.Log += (_, e) => { if (e.Module == "dvdnav") seen.Enqueue(e.Message); };
            using var media = new LibVLCSharp.Shared.Media(vlc, PlaybackTarget.DiscMrl(DiscKind.Dvd, share), LibVLCSharp.Shared.FromType.FromLocation);
            using var player = new LibVLCSharp.Shared.MediaPlayer(media);
            player.Play();
            Assert.True(Fixture.Within(Five, () => seen.Any(m => m.Contains("VIDEO_TS.IFO", StringComparison.Ordinal))), "VLC's disc reader never reached the folder: " + string.Join(" | ", seen.Take(6)));
            player.Stop();
        }
        finally { Directory.Delete(local, true); }
    }

    // A file that fails keeps the plate's own message: the stream words are for streams only.
    [Fact]
    public void A_broken_file_doesnt_get_the_stream_message()
    {
        string dir = Directory.CreateTempSubdirectory("stx-broken").FullName;
        try
        {
            string junk = Path.Combine(dir, "junk.mp4");
            File.WriteAllBytes(junk, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 31))]);
            using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
            engine.Open(junk);
            Fixture.Within(Five, () => engine.State is PlaybackState.Failed or PlaybackState.Ended);
            Assert.NotEqual(LibVlcEngine.StreamUnreachable, engine.Error);
            Assert.NotEqual(LibVlcEngine.StreamCutOff, engine.Error);
        }
        finally { Directory.Delete(dir, true); }
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
