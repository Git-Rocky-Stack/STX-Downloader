using System.Diagnostics;
using Stx.Core.Playback;

namespace Stx.Playback;

/// <summary>
/// Bit-perfect music (Phase 5): FFmpeg decodes the file's first audio stream to raw PCM at its own rate, channels and
/// depth, and <see cref="ExclusiveRenderer"/> hands it to the device it holds exclusively. Nothing between the decoder
/// and the device changes a sample: no resampler, no mixer, no volume. The device is let go when playback stops or ends.
/// </summary>
public sealed class MusicEngine(string ffmpeg) : IPlaybackEngine
{
    readonly object _gate = new();
    ExclusiveRenderer? _renderer;
    Process? _decoder;
    string _path = "";
    PcmSource? _source;
    TimeSpan _offset;
    PlaybackState _state = PlaybackState.Idle;
    // The decoder's last words on stderr, for the log when it dies before the end of the file.
    string _decoderSaid = "";
    string? _error;
    Timer? _clock;

    public event EventHandler? Changed;

    public PlaybackState State => _state;
    public string? Error => _error;
    public bool HasVideo => false;
    public int Volume { get => 100; set { } }
    public bool VolumeFixed => true;
    public IReadOnlyList<MediaTrack> AudioTracks => [];
    public IReadOnlyList<MediaTrack> SubtitleTracks => [];
    public IReadOnlyList<MediaTrack> Chapters => [];
    public TimeSpan Length => _source?.Seconds is double s ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;

    /// <summary>What was opened on the device, as the readout shows it.</summary>
    public string AudioReadout => _renderer is { } r ? BitPerfect.Readout(AudioRoute.Exclusive, _source?.Bits ?? r.Layout.Valid, r.Rate) : "";

    /// <summary>Frames handed to the device, less those still waiting in its buffer.</summary>
    public TimeSpan Position
    {
        get
        {
            if (_renderer is not { } r) return _offset;
            var played = TimeSpan.FromSeconds((double)r.FramesWritten / r.Rate) - r.Latency;
            var at = _offset + (played > TimeSpan.Zero ? played : TimeSpan.Zero);
            return Length > TimeSpan.Zero && at > Length ? Length : at;
        }
    }

    /// <summary>The Open used by the routing engine: the device is taken here, so a refusal can fall back before anything plays.</summary>
    /// <exception cref="ExclusiveUnavailableException">The device can't be had exclusively at the file's own format.</exception>
    public void Open(string path, PcmSource source)
    {
        lock (_gate)
        {
            Release();
            _path = path;
            _source = source;
            _offset = TimeSpan.Zero;
            _error = null;
            _renderer = ExclusiveRenderer.Open(source.Rate, source.Channels, BitPerfect.Candidates(source.Bits));
            _renderer.Finished += OnFinished;
            _renderer.Faulted += OnFaulted;
            Start(TimeSpan.Zero);
        }
    }

    void IPlaybackEngine.Open(string path) => throw new InvalidOperationException("The music engine needs the file's probe; RoutingEngine opens it.");

    void Start(TimeSpan from)
    {
        // The pump stops before the decoder it reads from is killed, or a seek would pull it out from under the read.
        _renderer!.Halt();
        try { _decoder?.Kill(true); } catch (InvalidOperationException) { }
        _decoder?.Dispose();
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in BitPerfect.FfmpegArgs(_path, _renderer!.Layout, from)) start.ArgumentList.Add(arg);
        _decoder = Process.Start(start)!;
        _decoderSaid = "";
        _decoder.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _decoderSaid = e.Data; };
        _decoder.BeginErrorReadLine();
        _offset = from;
        _renderer.Play(_decoder.StandardOutput.BaseStream);
        Set(PlaybackState.Playing);
        _clock ??= new Timer(_ => Raise(), null, 250, 250);
    }

    /// <summary>What the plate says when FFmpeg stops before the end of the file: it vanished, or it's damaged.</summary>
    public const string DecodeFailed = "THE FILE COULDN'T BE READ TO ITS END";

    void OnFinished(object? sender, int run)
    {
        bool failed;
        lock (_gate)
        {
            // Review 2026-11-07: an end queued just before a seek near the end used to stop the seek's new run.
            if (sender is not ExclusiveRenderer renderer || !ReferenceEquals(renderer, _renderer) || renderer.CurrentRun != run) return;
            // The stream ran dry. That is the end of the file only if FFmpeg says it finished: a file deleted or
            // damaged mid-play also runs the stream dry, and used to read as ENDED.
            failed = _decoder is { } decoder && decoder.WaitForExit(2000) && decoder.ExitCode != 0;
            if (failed)
            {
                Stx.Core.Logging.ErrorLog.Write("bit-perfect", $"FFmpeg exited {_decoder!.ExitCode} on {_path}: {_decoderSaid}");
                _offset = Position;
                _error = DecodeFailed;
            }
            else _offset = Length;
            Release();
        }
        Set(failed ? PlaybackState.Failed : PlaybackState.Ended);
    }

    // The device went away or the decoder died mid-play. The device goes back to Windows and the plate says so.
    void OnFaulted(object? sender, Exception e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _renderer)) return;
            _offset = Position;
            _error = "THE SOUND DEVICE STOPPED: " + e.Message.ToUpperInvariant();
            Release();
        }
        Set(PlaybackState.Failed);
    }

    public void TogglePause()
    {
        lock (_gate)
        {
            if (_renderer is null)
            {
                // RoutingEngine replays through its own Open, which falls back to shared. Called directly, a device
                // that can't be had still says why instead of throwing at the plate.
                if (_source is { } source && _state is PlaybackState.Ended or PlaybackState.Idle)
                {
                    try { Open(_path, source); }
                    catch (Exception e) when (e is ExclusiveUnavailableException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
                    {
                        // A decoder that failed to start may leave the device held: it goes back to Windows.
                        Release();
                        _error = e is ExclusiveUnavailableException ? e.Message : "THE DEVICE COULDN'T BE OPENED EXCLUSIVELY";
                        Set(PlaybackState.Failed);
                    }
                }
                return;
            }
            if (_state == PlaybackState.Paused) { _renderer.Resume(); Set(PlaybackState.Playing); }
            else { _renderer.Pause(); Set(PlaybackState.Paused); }
        }
    }

    public void Stop()
    {
        lock (_gate) { Release(); _offset = TimeSpan.Zero; }
        Set(PlaybackState.Idle);
    }

    public void Seek(TimeSpan to)
    {
        lock (_gate)
        {
            if (_renderer is null) return;
            bool paused = _state == PlaybackState.Paused;
            Start(to < TimeSpan.Zero ? TimeSpan.Zero : Length > TimeSpan.Zero && to > Length ? Length : to);
            if (paused) { _renderer.Pause(); Set(PlaybackState.Paused); }
        }
    }

    public void SelectAudio(int id) { }
    public void SelectSubtitle(int id) { }
    public void SelectChapter(int id) { }

    // The device goes back to Windows the moment nothing is playing, so other apps can make sound again.
    void Release()
    {
        _clock?.Dispose();
        _clock = null;
        try { _decoder?.Kill(true); } catch (InvalidOperationException) { }
        _decoder?.Dispose();
        _decoder = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    void Set(PlaybackState state)
    {
        _state = state;
        Raise();
    }

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        lock (_gate) Release();
        _state = PlaybackState.Idle;
    }
}
