using System.Diagnostics;
using Stx.Core.Playback;
using MediaTrack = Stx.Core.Playback.MediaTrack;

namespace Stx.Playback;

/// <summary>
/// The engine the PLAYER plate drives. Each file goes the way BIT-PERFECT and the file decide (Phase 5): lossless
/// music to <see cref="MusicEngine"/>, which holds the device exclusively; everything else to VLC's engine, with a
/// film's AC3, DTS or TrueHD passed through to the receiver when the engine started with passthrough on. When the
/// device can't be had exclusively, the file plays shared and the readout says why. It never fails silently.
/// </summary>
public sealed class RoutingEngine : IPlaybackEngine
{
    readonly LibVlcEngine _vlc;
    readonly Func<bool> _bitPerfect;
    readonly Func<string?> _ffmpeg, _ffprobe;
    readonly object _gate = new();
    MusicEngine? _music;
    IPlaybackEngine _active;
    string _sharedWhy = "";
    PlaybackState? _opening;
    int _generation;
    string? _path;
    bool _disposed;

    /// <param name="bitPerfect">The BIT-PERFECT setting, read at each open.</param>
    /// <param name="ffmpeg">FFmpeg and FFprobe, which first run downloads. Until they're there, everything plays through VLC's engine.</param>
    public RoutingEngine(LibVlcEngine vlc, Func<bool> bitPerfect, Func<string?> ffmpeg, Func<string?> ffprobe)
    {
        _vlc = vlc;
        _active = vlc;
        _bitPerfect = bitPerfect;
        _ffmpeg = ffmpeg;
        _ffprobe = ffprobe;
        _vlc.Changed += (_, _) => { if (ReferenceEquals(_active, _vlc)) Raise(); };
    }

    public event EventHandler? Changed;

    public PlaybackState State => _opening ?? _active.State;
    public TimeSpan Position => _active.Position;
    public TimeSpan Length => _active.Length;
    public bool HasVideo => _active.HasVideo;
    public string? Error => _active.Error;
    public int Volume { get => _vlc.Volume; set => _vlc.Volume = value; }
    public bool VolumeFixed => _active.VolumeFixed;
    public IReadOnlyList<MediaTrack> AudioTracks => _active.AudioTracks;
    public IReadOnlyList<MediaTrack> SubtitleTracks => _active.SubtitleTracks;
    public IReadOnlyList<MediaTrack> Chapters => _active.Chapters;

    public string AudioReadout
    {
        get
        {
            if (!_bitPerfect() || _opening is not null) return "";
            if (_active is MusicEngine music) return music.AudioReadout;
            if (_vlc.State is not (PlaybackState.Playing or PlaybackState.Paused)) return "";
            if (_sharedWhy.Length > 0) return _sharedWhy;
            // Passthrough: LibVLC logs the bitstream format it sends, such as 'spdi' or 'eac3'.
            return LibVlcEngine.IsBitstream(_vlc.AudioOutput) ? BitPerfect.Readout(AudioRoute.Passthrough, 0, 0) : "SHARED: THE DEVICE DIDN'T TAKE THE BITSTREAM";
        }
    }

    public void Open(string path)
    {
        int generation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = ++_generation;
            _path = path;
            _opening = PlaybackState.Opening;
            _sharedWhy = "";
        }
        Raise();
        // Probing runs ffprobe, which takes a moment; never on the caller's thread.
        Task.Run(() =>
        {
            // Anything thrown here would vanish into the task and leave the plate on OPENING. It plays shared instead.
            AudioRoute route;
            PcmSource? source;
            string why;
            try { (route, source, why) = Decide(path); }
            catch (Exception e) when (e is not OutOfMemoryException) { (route, source, why) = (AudioRoute.Shared, null, UnreadableSound); }
            // Everything from here runs under the lock, the hand-off to VLC's engine included, so a STOP, a newer open
            // or closing the app (each a new generation) can't be overtaken by this one finishing late.
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
                if (route == AudioRoute.Exclusive && source is not null)
                {
                    try
                    {
                        _vlc.Stop();
                        _music ??= NewMusic();
                        _music.Open(path, source);
                        _active = _music;
                        _opening = null;
                        Raise();
                        return;
                    }
                    catch (Exception e) when (e is ExclusiveUnavailableException or System.Runtime.InteropServices.COMException or System.ComponentModel.Win32Exception)
                    {
                        why = e is ExclusiveUnavailableException ? e.Message : "SHARED: THE DEVICE COULDN'T BE OPENED EXCLUSIVELY";
                    }
                }
                _music?.Stop();
                _active = _vlc;
                _sharedWhy = route == AudioRoute.Passthrough && !_vlc.Passthrough ? "SHARED: PASSTHROUGH STARTS WITH STX'S NEXT LAUNCH" : why;
                _opening = null;
                _vlc.Open(path);
            }
        });
    }

    MusicEngine NewMusic()
    {
        var music = new MusicEngine(_ffmpeg()!);
        music.Changed += (_, _) => { if (ReferenceEquals(_active, music)) Raise(); };
        return music;
    }

    (AudioRoute Route, PcmSource? Source, string Why) Decide(string path)
    {
        if (!_bitPerfect()) return (AudioRoute.Shared, null, "");
        if (StreamLink.IsUrl(path) || Directory.Exists(path)) return (AudioRoute.Shared, null, "SHARED: STREAMS AND DISCS PLAY THROUGH VLC'S ENGINE");
        if (_ffmpeg() is not { } ffmpeg || !File.Exists(ffmpeg) || _ffprobe() is not { } ffprobe || !File.Exists(ffprobe))
            return (AudioRoute.Shared, null, "SHARED: FFMPEG ISN'T DOWNLOADED YET");
        var source = Probe(ffprobe, path);
        var route = BitPerfect.Route(true, source);
        return (route, source, route == AudioRoute.Shared ? BitPerfect.WhyShared(source) : "");
    }

    const string UnreadableSound = "SHARED: STX COULDN'T READ THE FILE'S SOUND";
    static readonly TimeSpan ProbeLimit = TimeSpan.FromSeconds(10);

    static PcmSource? Probe(string ffprobe, string path)
    {
        var start = new ProcessStartInfo(ffprobe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path }) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start)!;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            // Read while waiting, so the limit holds: a ReadToEnd first would wait as long as a stalled share does.
            var json = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(ProbeLimit))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return null;
            }
            return PcmSource.FromProbe(json.GetAwaiter().GetResult());
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>PLAY on a finished or stopped lossless track opens it again through the routing, so a device another
    /// app took in the meantime means shared sound with the reason, never an error thrown at the plate.</summary>
    public void TogglePause()
    {
        string? replay;
        lock (_gate)
        {
            if (_disposed || _opening is not null) return;
            replay = _active is MusicEngine { State: PlaybackState.Ended or PlaybackState.Idle } ? _path : null;
        }
        if (replay is not null) Open(replay);
        else _active.TogglePause();
    }

    /// <summary>Stops what's playing, and calls off a file still being probed, which would otherwise start after STOP.</summary>
    public void Stop()
    {
        bool wasOpening;
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            wasOpening = _opening is not null;
            _opening = null;
        }
        _active.Stop();
        if (wasOpening) Raise();
    }

    public void Seek(TimeSpan to)
    {
        if (_opening is null) _active.Seek(to);
    }
    public void SelectAudio(int id) => _active.SelectAudio(id);
    public void SelectSubtitle(int id) => _active.SelectSubtitle(id);
    public void SelectChapter(int id) => _active.SelectChapter(id);

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
        }
        _music?.Dispose();
        _vlc.Dispose();
    }
}
