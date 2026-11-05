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

    /// <summary>Something above changed. May fire on any thread.</summary>
    event EventHandler? Changed;
}
