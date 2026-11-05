namespace Stx.Core.Playback;

public enum PlaybackState { Idle, Opening, Playing, Paused, Ended, Failed }

public sealed record MediaTrack(int Id, string Name);

/// <summary>What the console needs from a media player. The LibVLC engine in Stx.Playback is the real one.</summary>
public interface IPlaybackEngine : IDisposable
{
    PlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Length { get; }
    bool HasVideo { get; }

    /// <summary>A plain message for the plate when the engine refuses or fails a file, or null to use the console's own.</summary>
    string? Error { get; }

    /// <summary>0 to 100.</summary>
    int Volume { get; set; }

    IReadOnlyList<MediaTrack> AudioTracks { get; }
    IReadOnlyList<MediaTrack> SubtitleTracks { get; }
    IReadOnlyList<MediaTrack> Chapters { get; }

    /// <summary>Opens the file and starts playing it, replacing whatever was playing.</summary>
    void Open(string path);
    void TogglePause();
    void Stop();
    void Seek(TimeSpan to);
    void SelectAudio(int id);
    void SelectSubtitle(int id);
    void SelectChapter(int id);

    /// <summary>What reaches the sound device, for the plate's readout ("24/96 BIT-PERFECT", "SHARED WITH WINDOWS"). Empty when there's nothing to say.</summary>
    string AudioReadout => "";

    /// <summary>True while the volume can't change: a bit-perfect stream is sent at full scale, untouched.</summary>
    bool VolumeFixed => false;

    /// <summary>Something above changed. May fire on any thread.</summary>
    event EventHandler? Changed;
}
