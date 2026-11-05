using System.Collections.Concurrent;
using LibVLCSharp.Shared;
using Stx.Core.Playback;
using MediaTrack = Stx.Core.Playback.MediaTrack;

namespace Stx.Playback;

/// <summary>LibVLC's counts for the open file: pictures and audio blocks decoded, pictures shown, and pictures lost for being late.</summary>
public readonly record struct Decoded(int Pictures, int AudioBlocks, int Shown = 0, int Lost = 0);

/// <summary>
/// The player on VLC's engine. A headless engine starts LibVLC at once with dummy outputs. An engine with a
/// screen waits for the video view's swap chain (spike S1): LibVLC can only draw into it if it starts with it,
/// so an <see cref="Open"/> that comes first is held and played when <see cref="UseSwapChain"/> arrives.
/// </summary>
public sealed class LibVlcEngine : IPlaybackEngine
{
    /// <summary>What the plate says for Dirac / VC-2, which crashes VLC 3.0.24's engine (the official desktop build too).</summary>
    public const string DiracRefused = "DIRAC VIDEO CAN'T BE PLAYED SAFELY";

    /// <summary>The General MIDI SoundFont shipped beside the engine (tools/vlc/engine.json).</summary>
    public static string ShippedSoundFont => Path.Combine(AppContext.BaseDirectory, "libvlc", "soundfonts", "FluidR3_GM.sf2");

    static readonly Lazy<bool> NativeReady = new(() => { LibVLCSharp.Shared.Core.Initialize(); return true; });

    readonly bool _headless;
    readonly bool _realAudio;
    readonly object _gate = new();
    readonly ConcurrentQueue<string> _errors = new();
    LibVLC? _vlc;
    MediaPlayer? _player;
    Media? _media;
    string? _pending;
    PlaybackState _state = PlaybackState.Idle;
    string? _error;
    int _volume = 100;
    bool _sawHardwareDecoder;
    bool _sawVideoDecoder;
    bool _disposed;
    int _generation;
    volatile bool _monoRecovery;
    bool _monoRestarted;

    public LibVlcEngine(PerformanceProfile profile, bool headless = false, bool realAudio = false)
    {
        Profile = profile;
        _headless = headless;
        _realAudio = realAudio;
        if (headless) Start(null);
    }

    /// <summary>The PERFORMANCE profile. A change applies from the next <see cref="Open"/>.</summary>
    public PerformanceProfile Profile { get; set; }

    /// <summary>The native player, for the video view. Null until LibVLC has started, and after Dispose.</summary>
    public MediaPlayer? Player => _player;

    /// <summary>Raised when <see cref="Player"/> is created, so a video view can take it.</summary>
    public event EventHandler? PlayerCreated;

    public event EventHandler? Changed;

    /// <summary>"d3d11va" or "software" once a picture is being decoded; empty before that, and for sound only.</summary>
    // LibVLC picks a hardware decoder, if any, before the first picture, so a decoded picture with no hardware
    // decoder named means software.
    public string Decoder => _sawHardwareDecoder ? "d3d11va" : _sawVideoDecoder && Decoded.Pictures > 0 ? "software" : "";

    // LibVLC 3.0 logs this once for every output it doesn't own a window for: display.c:1171-1173 always asks the new
    // display for its window state, and win32/common.c:697-699 refuses that for external rendering (a swap chain or
    // memory). It changes nothing and no option avoids it, so it isn't counted as an error.
    static readonly string[] Harmless = ["main: Failed to set on top"];

    /// <summary>How much of the open file has been decoded so far, from LibVLC's own statistics.</summary>
    public Decoded Decoded => _media?.Statistics is { } st ? new Decoded(st.DecodedVideo, st.DecodedAudio, st.DisplayedPictures, st.LostPictures) : default;

    /// <summary>The format the sound leaves in, as LibVLC logs it, for example "'f32l' 48000 Hz 3F2R/LFE". Empty before sound starts.</summary>
    public string AudioOutput { get; private set; } = "";

    /// <summary>Which Windows audio path carries the sound: "wasapi" or "directsound". Empty before sound starts.</summary>
    public string AudioStream { get; private set; } = "";

    /// <summary>Errors LibVLC logged since the last <see cref="Open"/>, newest last.</summary>
    public IReadOnlyList<string> LoggedErrors => [.. _errors];

    public PlaybackState State => _state;
    public string? Error => _error;
    public TimeSpan Position => _player is { } p && p.Time > 0 ? TimeSpan.FromMilliseconds(p.Time) : TimeSpan.Zero;
    public TimeSpan Length => _player is { } p && p.Length > 0 ? TimeSpan.FromMilliseconds(p.Length) : TimeSpan.Zero;
    public bool HasVideo => _player is { } p && p.VideoTrackCount > 0;

    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            if (_player is { } p) p.Volume = _volume;
            Raise();
        }
    }

    public IReadOnlyList<MediaTrack> AudioTracks => Tracks(_player?.AudioTrackDescription);
    public IReadOnlyList<MediaTrack> SubtitleTracks => Tracks(_player?.SpuDescription);
    public IReadOnlyList<MediaTrack> Chapters =>
        _player?.FullChapterDescriptions(-1) is { Length: > 0 } chapters
            ? [.. chapters.Select((c, i) => new MediaTrack(i, string.IsNullOrWhiteSpace(c.Name) ? $"Chapter {i + 1}" : c.Name))]
            : [];

    // LibVLC lists "Disable" as id -1; the plate offers its own off choice.
    static MediaTrack[] Tracks(LibVLCSharp.Shared.Structures.TrackDescription[]? tracks) =>
        tracks is null ? [] : [.. tracks.Where(t => t.Id >= 0).Select(t => new MediaTrack(t.Id, t.Name ?? $"Track {t.Id}"))];

    /// <summary>Called by the video view once its swap chain exists. Starts LibVLC on it and plays anything held.</summary>
    public void UseSwapChain(IReadOnlyList<string> swapChainOptions)
    {
        if (_headless) return;
        string? held;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_vlc is not null) return;
            Start(swapChainOptions);
            held = _pending;
            _pending = null;
        }
        if (held is not null) Play(held);
    }

    public void Open(string path)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _error = null;
            _errors.Clear();
            _sawHardwareDecoder = _sawVideoDecoder = false;
            AudioOutput = AudioStream = "";
            _state = PlaybackState.Opening;
            _generation++;
            if (_player is null) _pending = path;
        }
        Raise();
        if (_player is not null) Play(path);
    }

    // Off the caller's thread: the file's tracks are read first, which takes a moment.
    void Play(string path)
    {
        int generation = _generation;
        _ = Task.Run(() =>
        {
            Media media;
            lock (_gate)
            {
                if (_disposed || _vlc is null || generation != _generation) return;
                media = new Media(_vlc, path, FromType.FromPath, LibVlcOptions.ForMedia(Profile));
            }
            var (mono, dirac) = Inspect(media);
            if (dirac)
            {
                media.Dispose();
                lock (_gate) { if (_disposed || generation != _generation) return; _error = DiracRefused; }
                Set(PlaybackState.Failed);
                return;
            }
            lock (_gate)
            {
                if (_disposed || _player is null || generation != _generation) { media.Dispose(); return; }
                // Windows refuses a one-channel stream in shared mode (0x88890008, AUDCLNT_E_UNSUPPORTED_FORMAT) and
                // LibVLC 3.0.24 then falls back to DirectSound. The stereo setting can't be made to stick before the
                // output opens (LibVLC resets it there), so a mono file starts muted; when the output lands on
                // DirectSound it is switched to stereo, which reopens it on WASAPI, and then sound is let through.
                // Anything else keeps its own layout, so 5.1 and 7.1 are never downmixed.
                _monoRecovery = mono;
                _monoRestarted = false;
                _player.Mute = mono;
                var old = _media;
                _media = media;
                _player.Play(media);
                old?.Dispose();
            }
        });
    }

    // 'drac', as LibVLC names Dirac and VC-2.
    const uint Dirac = 'd' | 'r' << 8 | 'a' << 16 | 'c' << 24;

    // Reads the file's tracks before anything is decoded: mono sound needs the stereo path, and Dirac must never reach
    // the decoder, because VLC 3.0.24 crashes the process on it.
    static (bool Mono, bool Dirac) Inspect(Media media)
    {
        try
        {
            media.Parse(MediaParseOptions.ParseLocal, timeout: 3000).Wait(4000);
            var tracks = media.Tracks;
            var audio = tracks.Where(t => t.TrackType == TrackType.Audio).ToList();
            return (audio.Count > 0 && audio.All(t => t.Data.Audio.Channels == 1),
                    tracks.Any(t => t.TrackType == TrackType.Video && t.Codec == Dirac));
        }
        catch (AggregateException) { return (false, false); }
        catch (VLCException) { return (false, false); }
    }

    public void TogglePause()
    {
        if (_player is not { } p) return;
        if (_state == PlaybackState.Ended && _media is { } m) { p.Stop(); p.Play(m); return; }
        p.Pause();
    }

    public void Stop()
    {
        lock (_gate) _pending = null;
        _player?.Stop();
        Set(PlaybackState.Idle);
    }

    public void Seek(TimeSpan to)
    {
        if (_player is { } p && p.IsSeekable) p.Time = (long)Math.Max(0, to.TotalMilliseconds);
    }

    int _hiddenVideoTrack = -1;

    /// <summary>Stops decoding pictures, for a window hidden in the tray. The sound carries on.</summary>
    public void HideVideo()
    {
        if (_player is not { } p || p.VideoTrack < 0) return;
        _hiddenVideoTrack = p.VideoTrack;
        p.SetVideoTrack(-1);
    }

    /// <summary>Starts decoding pictures again after <see cref="HideVideo"/>.</summary>
    public void ShowVideo()
    {
        if (_player is not { } p || _hiddenVideoTrack < 0) return;
        p.SetVideoTrack(_hiddenVideoTrack);
        _hiddenVideoTrack = -1;
    }

    public void SelectAudio(int id) => _player?.SetAudioTrack(id);
    public void SelectSubtitle(int id) => _player?.SetSpu(id);
    public void SelectChapter(int id) { if (_player is { } p) p.Chapter = id; }

    void Start(IReadOnlyList<string>? swapChain)
    {
        _ = NativeReady.Value;
        // Debug level, so the decoder LibVLC picks can be read from its log.
        string? soundFont = File.Exists(ShippedSoundFont) ? ShippedSoundFont : null;
        var vlc = new LibVLC(enableDebugLogs: true, LibVlcOptions.For(_headless, swapChain, _realAudio, soundFont));
        vlc.Log += OnLog;
        var player = new MediaPlayer(vlc) { Volume = _volume };
        if (_headless) DrawIntoMemory(player);
        player.Opening += (_, _) => Set(PlaybackState.Opening);
        player.Playing += (_, _) => Set(PlaybackState.Playing);
        player.Paused += (_, _) => Set(PlaybackState.Paused);
        player.EndReached += (_, _) => Set(PlaybackState.Ended);
        player.EncounteredError += (_, _) => Fail();
        player.TimeChanged += (_, _) => Raise();
        player.LengthChanged += (_, _) => Raise();
        player.ESAdded += (_, _) => Raise();
        _vlc = vlc;
        _player = player;
        PlayerCreated?.Invoke(this, EventArgs.Empty);
    }

    // Headless pictures land in one small buffer that nothing reads. LibVLC's dummy video output would be the obvious
    // choice, but in 3.0.24 it crashes the process (0xC0000005) when an H.264 file reaches its end.
    const int MemoryWidth = 160, MemoryHeight = 90;
    IntPtr _memory;

    void DrawIntoMemory(MediaPlayer player)
    {
        _memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(MemoryWidth * MemoryHeight * 4);
        IntPtr memory = _memory;
        player.SetVideoFormat("RV32", MemoryWidth, MemoryHeight, MemoryWidth * 4);
        player.SetVideoCallbacks((IntPtr _, IntPtr planes) =>
        {
            System.Runtime.InteropServices.Marshal.WriteIntPtr(planes, memory);
            return IntPtr.Zero;
        }, null, null);
    }

    void OnLog(object? sender, LogEventArgs e)
    {
        if (e.Level == LogLevel.Error && !Harmless.Contains($"{e.Module}: {e.Message}"))
        {
            _errors.Enqueue($"{e.Module}: {e.Message}");
            while (_errors.Count > 200) _errors.TryDequeue(out _);
        }
        if (e.Module == "main")
        {
            if (e.Message.StartsWith("using video decoder module", StringComparison.Ordinal)) _sawVideoDecoder = true;
            const string Output = "output '", Stream = "using aout stream module \"";
            if (e.Message.StartsWith(Output, StringComparison.Ordinal) && e.Message.IndexOf(" frame=", StringComparison.Ordinal) is > 0 and var end)
                AudioOutput = e.Message[(Output.Length - 1)..end];
            else if (e.Message.StartsWith(Stream, StringComparison.Ordinal))
            {
                AudioStream = e.Message[Stream.Length..].TrimEnd('"');
                if (_monoRecovery) RecoverMono(AudioStream);
            }
        }
        if (e.Module == "avcodec")
        {
            if (e.Message.Contains("for hardware decoding", StringComparison.Ordinal)) _sawHardwareDecoder = true;
        }
    }

    // Runs from LibVLC's log thread, so the player is called from a task, never from here.
    void RecoverMono(string stream)
    {
        var player = _player;
        if (player is null) return;
        if (stream == "directsound" && !_monoRestarted)
        {
            _monoRestarted = true;
            Task.Run(() => player.SetChannel(AudioOutputChannel.Stereo));
            return;
        }
        _monoRecovery = false;
        Task.Run(() => player.Mute = false);
    }

    // LibVLC's own words stay in LoggedErrors; the plate gets the console's plain message.
    void Fail()
    {
        _error = null;
        Set(PlaybackState.Failed);
    }

    void Set(PlaybackState state)
    {
        if (_disposed) return;
        _state = state;
        Raise();
    }

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        MediaPlayer? player;
        LibVLC? vlc;
        Media? media;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = null;
            (player, vlc, media) = (_player, _vlc, _media);
            _player = null;
            _vlc = null;
            _media = null;
            _state = PlaybackState.Idle;
        }
        player?.Stop();
        player?.Dispose();
        media?.Dispose();
        if (vlc is not null)
        {
            vlc.Log -= OnLog;
            vlc.Dispose();
        }
        if (_memory != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(_memory);
            _memory = IntPtr.Zero;
        }
    }
}
