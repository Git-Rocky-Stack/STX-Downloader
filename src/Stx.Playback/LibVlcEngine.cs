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
    public const string DiracRefused = DiracSniff.Refused;

    // Reads the start of an http(s) stream for Dirac before LibVLC is given it (DiracSniff.InStreamAsync).
    static readonly HttpClient StreamCheck = new();

    /// <summary>What the plate says when a stream fails before a sound or picture arrived: the address answered with
    /// nothing STX can play, or didn't answer at all.</summary>
    public const string StreamUnreachable = "THAT STREAM COULDN'T BE OPENED. CHECK THE ADDRESS AND YOUR CONNECTION.";

    /// <summary>What the plate says when a stream that was playing fails: the connection or the station went away.</summary>
    public const string StreamCutOff = "THE STREAM WAS CUT OFF. CHECK YOUR CONNECTION, THEN PLAY IT AGAIN.";

    /// <summary>The General MIDI SoundFont shipped beside the engine: FluidR3_GM as SoundFont 3 (tools/vlc/engine.json).</summary>
    public static string ShippedSoundFont => Path.Combine(AppContext.BaseDirectory, "libvlc", "soundfonts", "FluidR3_GM.sf3");

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
    TargetKind _targetKind;
    volatile bool _reachedPlaying;
    volatile bool _monoRecovery;
    bool _monoRestarted;

    public LibVlcEngine(PerformanceProfile profile, bool headless = false, bool realAudio = false)
    {
        Profile = profile;
        _headless = headless;
        _realAudio = realAudio;
        if (headless) Start(null);
    }

    /// <summary>Opens files paused at their start (LibVLC's start-paused). No picture is drawn until the player steps a frame; the screenshot galleries do that so every capture shows the same first frame.</summary>
    public bool StartPaused { get; set; }

    /// <summary>
    /// ENHANCE (Phase 6), already held to PERFORMANCE's ceiling and the GPU. A change restarts LibVLC in place: the
    /// video view takes the new player, and whatever was playing carries on from where it was.
    /// </summary>
    public EnhanceLevel Enhance
    {
        get => _enhance;
        set
        {
            if (value == _enhance) return;
            _enhance = value;
            Restart();
        }
    }

    /// <summary>What VLC's own log says is running on the picture: SHARPEN once its filter loads, UPSCALE once the super
    /// resolution scaler starts, OFF when VLC refused it. Null until VLC has said.</summary>
    public EnhanceLevel? EnhanceSeen { get; private set; }

    EnhanceLevel _enhance;
    IReadOnlyList<string>? _swapChain;
    string? _lastPath;
    TimeSpan? _resumeAt;
    bool _resumePaused;

    void Restart()
    {
        string? path;
        // An open still reading a file's tracks takes the lock; let it finish first, outside it.
        try { _opening.Wait(TimeSpan.FromSeconds(6)); } catch (AggregateException) { }
        lock (_gate)
        {
            if (_disposed || _vlc is null) return;
            path = _state is PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Opening ? _lastPath : null;
            if (path is not null)
            {
                _resumeAt = Position;
                _resumePaused = _state == PlaybackState.Paused;
            }
            var (player, vlc, media) = (_player, _vlc, _media);
            _player = null;
            _vlc = null;
            _media = null;
            player?.Stop();
            player?.Dispose();
            media?.Dispose();
            if (vlc is not null) { vlc.Log -= OnLog; vlc.Dispose(); }
            EnhanceSeen = null;
            Start(_swapChain);
        }
        if (path is not null) Open(path);
        else Raise();
    }

    /// <summary>Whether LibVLC starts with bitstream passthrough (BIT-PERFECT's film route). Set before the swap chain arrives; read once.</summary>
    public bool Passthrough { get; set; }

    /// <summary>Whether a logged output format is a bitstream sent to a receiver rather than PCM: 'spdi'/'spdb' (S/PDIF), or E-AC3, DTS or TrueHD over HDMI.</summary>
    public static bool IsBitstream(string audioOutput) =>
        audioOutput.StartsWith("'spd", StringComparison.Ordinal) || audioOutput.StartsWith("'eac3'", StringComparison.Ordinal)
        || audioOutput.StartsWith("'dts ", StringComparison.Ordinal) || audioOutput.StartsWith("'mlp ", StringComparison.Ordinal)
        || audioOutput.StartsWith("'trhd'", StringComparison.Ordinal) || audioOutput.StartsWith("'a52 '", StringComparison.Ordinal);

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

    /// <summary>The last path or address LibVLC was given, or null if it has been given nothing. A refused disc never gets here.</summary>
    public string? HandedToLibVlc { get; private set; }

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
            _reachedPlaying = false;
            _generation++;
            _lastPath = path;
            if (_player is null) _pending = path;
        }
        Raise();
        if (_player is not null) Play(path);
    }

    // Off the caller's thread: the file's tracks are read first, which takes a moment.
    Task _opening = Task.CompletedTask;

    void Play(string path)
    {
        int generation = _generation;
        _opening = Task.Run(async () =>
        {
            Media? media = null;
            try
            {
                // STX decides what this is before LibVLC sees it. An encrypted disc, or an address that would need a
                // listening port, is refused here and never opened (Phase 4).
                var target = PlaybackTarget.For(path);
                if (target.IsRefused)
                {
                    lock (_gate) { if (_disposed || generation != _generation) return; _error = target.Refusal; }
                    Set(PlaybackState.Failed);
                    return;
                }
                lock (_gate)
                {
                    if (_disposed || _vlc is null || generation != _generation) return;
                    string[] options = [.. LibVlcOptions.ForMedia(Profile), .. LibVlcOptions.ForTarget(target.Kind), .. StartPaused ? [":start-paused"] : Array.Empty<string>()];
                    // The target's location, not the path as given: a file: address arrives here as the file it names.
                    media = target.Kind == TargetKind.File
                        ? new Media(_vlc, target.Location, FromType.FromPath, options)
                        : new Media(_vlc, target.Location, FromType.FromLocation, options);
                    HandedToLibVlc = target.Location;
                    _targetKind = target.Kind;
                }
                var (mono, dirac) = Inspect(media, target);
                // LibVLC reads local files' tracks only, so a stream is checked from its first bytes (review 2026-11-07).
                if (!dirac && target.Kind == TargetKind.Stream) dirac = await DiracSniff.InStreamAsync(StreamCheck, target.Location).ConfigureAwait(false);
                if (dirac)
                {
                    media.Dispose();
                    media = null;
                    lock (_gate) { if (_disposed || generation != _generation) return; _error = DiracRefused; }
                    Set(PlaybackState.Failed);
                    return;
                }
                lock (_gate)
                {
                    if (_disposed || _player is null || generation != _generation) { media.Dispose(); media = null; return; }
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
                    media = null;
                    bool started = _player.Play(_media);
                    old?.Dispose();
                    if (!started) { _error = null; _state = PlaybackState.Failed; }
                }
                if (_state == PlaybackState.Failed) Raise();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Review 2026-11-07: a throw here vanished into the task and left the plate on OPENING for good.
                media?.Dispose();
                Stx.Core.Logging.ErrorLog.Write("player", e);
                lock (_gate) { if (_disposed || generation != _generation) return; _error = null; }
                Set(PlaybackState.Failed);
            }
        });
    }

    // 'drac', as LibVLC names Dirac and VC-2.
    const uint Dirac = 'd' | 'r' << 8 | 'a' << 16 | 'c' << 24;

    // Reads the file's tracks before anything is decoded: mono sound needs the stereo path, and Dirac must never reach
    // the decoder, because VLC 3.0.24 crashes the process on it. When LibVLC can't finish reading a file's tracks (a
    // slow share, a parse that times out or throws), the guard doesn't assume the best: the file's first megabyte is
    // checked for Dirac's own signature instead (review 2026-11-07).
    static (bool Mono, bool Dirac) Inspect(Media media, PlaybackTarget target)
    {
        bool parsed = false;
        try
        {
            var parsing = media.Parse(MediaParseOptions.ParseLocal, timeout: 3000);
            parsed = parsing.Wait(4000) && parsing.Result == MediaParsedStatus.Done;
            var tracks = media.Tracks;
            var audio = tracks.Where(t => t.TrackType == TrackType.Audio).ToList();
            bool dirac = tracks.Any(t => t.TrackType == TrackType.Video && t.Codec == Dirac);
            if (!dirac && !parsed && target.Kind == TargetKind.File) dirac = DiracSniff.Present(target.Location);
            return (audio.Count > 0 && audio.All(t => t.Data.Audio.Channels == 1), dirac);
        }
        catch (Exception e) when (e is AggregateException or VLCException)
        {
            return (false, target.Kind == TargetKind.File && DiracSniff.Present(target.Location));
        }
    }

    public void TogglePause()
    {
        if (_player is not { } p) return;
        if (_state == PlaybackState.Ended && _media is { } m) { p.Stop(); p.Play(m); return; }
        p.Pause();
    }

    public void Stop()
    {
        // A file still being opened is called off: without the new generation it would start after STOP.
        lock (_gate) { _pending = null; _generation++; }
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
        _swapChain = swapChain;
        // Debug level, so the decoder LibVLC picks can be read from its log.
        string? soundFont = File.Exists(ShippedSoundFont) ? ShippedSoundFont : null;
        var vlc = new LibVLC(enableDebugLogs: true, LibVlcOptions.For(_headless, swapChain, _realAudio, soundFont, Passthrough, _enhance));
        vlc.Log += OnLog;
        var player = new MediaPlayer(vlc) { Volume = _volume };
        if (_headless) DrawIntoMemory(player);
        player.Opening += (_, _) => Set(PlaybackState.Opening);
        player.Playing += (_, _) => { _reachedPlaying = true; Set(PlaybackState.Playing); Resume(player); };
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

    // After an ENHANCE restart, the file carries on from where it was, paused if it was paused.
    void Resume(MediaPlayer player)
    {
        if (_resumeAt is not { } at) return;
        _resumeAt = null;
        bool paused = _resumePaused;
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(player, _player)) return;
                if (player.IsSeekable) player.Time = (long)at.TotalMilliseconds;
                if (paused) player.SetPause(true);
            }
        });
    }

    // Headless pictures land in one small buffer that nothing reads. LibVLC's dummy video output would be the obvious
    // choice, but in 3.0.24 it crashes the process (0xC0000005) when an H.264 file reaches its end.
    const int MemoryWidth = 160, MemoryHeight = 90;
    IntPtr _memory;

    void DrawIntoMemory(MediaPlayer player)
    {
        if (_memory == IntPtr.Zero) _memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(MemoryWidth * MemoryHeight * 4);
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
        // ENHANCE, as VLC reports it: the sharpen filter loading, or the Direct3D 11 output starting its scaler or refusing it.
        if (e.Message.Contains("video filter module \"sharpen\"", StringComparison.Ordinal)) EnhanceSeen = EnhanceLevel.Sharpen;
        else if (e.Message.StartsWith("Using Super Resolution scaler", StringComparison.Ordinal)) EnhanceSeen = EnhanceLevel.Upscale;
        else if (e.Message.StartsWith("Super Resolution filter not supported", StringComparison.Ordinal)) EnhanceSeen = EnhanceLevel.Off;
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
            Task.Run(() => { lock (_gate) { if (!_disposed && ReferenceEquals(player, _player)) player.SetChannel(AudioOutputChannel.Stereo); } });
            return;
        }
        _monoRecovery = false;
        Task.Run(() => { lock (_gate) { if (!_disposed && ReferenceEquals(player, _player)) player.Mute = false; } });
    }

    // LibVLC's own words stay in LoggedErrors; the plate gets the console's plain message. A file that fails gets the
    // plate's own "couldn't be played"; a stream says whether it never arrived or was cut off (handoff 2026-11-05, item 3).
    void Fail()
    {
        _error = _targetKind == TargetKind.Stream ? (_reachedPlaying ? StreamCutOff : StreamUnreachable) : null;
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
        // An open still reading the file's tracks uses LibVLC; let it see _disposed and finish before LibVLC goes.
        try { _opening.Wait(TimeSpan.FromSeconds(6)); } catch (AggregateException) { }
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
