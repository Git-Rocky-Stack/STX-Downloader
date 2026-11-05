using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Playback;

namespace Stx.ViewModels;

/// <summary>
/// The PLAYER plate's state. It drives a playback engine and reads it back after every change the engine reports.
/// The engine reports from its own threads, so every read goes through <c>onUi</c>.
/// </summary>
public sealed partial class PlayerViewModel : ObservableObject, IDisposable
{
    /// <summary>Shown when the engine fails without a message of its own.</summary>
    public const string CouldNotPlay = "THIS FILE COULDN'T BE PLAYED.";

    // Previous goes back to the start of the file after this much of it has played; before that, to the file before.
    static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(3);

    readonly IPlaybackEngine _engine;
    readonly Action<Action> _onUi;
    IReadOnlyList<(string Path, string Title)> _list = [];
    int _index = -1;
    bool _disposed;

    public PlayerViewModel(IPlaybackEngine engine, Action<Action> onUi)
    {
        _engine = engine;
        _onUi = onUi;
        _engine.Changed += OnEngineChanged;
    }

    string _title = "", _timeText = "00:00:00", _problem = "";
    bool _hasMedia, _isPlaying, _isAudioOnly, _isFullScreen;
    double _progress;

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public bool HasMedia { get => _hasMedia; private set => SetProperty(ref _hasMedia, value); }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    public bool IsAudioOnly { get => _isAudioOnly; private set => SetProperty(ref _isAudioOnly, value); }
    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Problem { get => _problem; private set => SetProperty(ref _problem, value); }
    public bool IsFullScreen { get => _isFullScreen; set => SetProperty(ref _isFullScreen, value); }

    public int Volume
    {
        get => _engine.Volume;
        set { if (_engine.Volume == value) return; _engine.Volume = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<MediaTrack> AudioTracks => _engine.AudioTracks;
    public IReadOnlyList<MediaTrack> SubtitleTracks => _engine.SubtitleTracks;
    public IReadOnlyList<MediaTrack> Chapters => _engine.Chapters;

    /// <summary>Plays <paramref name="path"/>, replacing whatever was playing. The list is what PREV and NEXT walk.</summary>
    public void Play(string path, string title, IReadOnlyList<(string Path, string Title)> list)
    {
        _list = list;
        _index = Enumerable.Range(0, list.Count).FirstOrDefault(i => string.Equals(list[i].Path, path, StringComparison.OrdinalIgnoreCase), -1);
        Title = title;
        _engine.Open(path);
        Refresh();
    }

    [RelayCommand]
    void PlayPause()
    {
        if (_engine.State == PlaybackState.Idle && _index >= 0) PlayAt(_index);
        else _engine.TogglePause();
        Refresh();
    }

    [RelayCommand]
    void Stop()
    {
        _engine.Stop();
        Refresh();
    }

    [RelayCommand]
    void Previous()
    {
        if (_engine.Position > RestartAfter || _index <= 0) _engine.Seek(TimeSpan.Zero);
        else PlayAt(_index - 1);
        Refresh();
    }

    [RelayCommand]
    void Next()
    {
        if (_index >= 0 && _index < _list.Count - 1) PlayAt(_index + 1);
        else _engine.Stop();
        Refresh();
    }

    [RelayCommand]
    void FullScreen() => IsFullScreen = !IsFullScreen;

    /// <summary>Jumps to a fraction of the way through, 0 to 1.</summary>
    [RelayCommand]
    void Seek(double fraction) => _engine.Seek(_engine.Length * Math.Clamp(fraction, 0, 1));

    public void SelectAudio(int id) => _engine.SelectAudio(id);
    public void SelectSubtitle(int id) => _engine.SelectSubtitle(id);
    public void SelectChapter(int id) => _engine.SelectChapter(id);

    void PlayAt(int index)
    {
        _index = index;
        Title = _list[index].Title;
        _engine.Open(_list[index].Path);
    }

    void OnEngineChanged(object? sender, EventArgs e) => _onUi(Refresh);

    void Refresh()
    {
        if (_disposed) return;
        var state = _engine.State;
        HasMedia = state is not PlaybackState.Idle;
        IsPlaying = state is PlaybackState.Playing;
        IsAudioOnly = state is PlaybackState.Playing or PlaybackState.Paused && !_engine.HasVideo;
        var position = _engine.Position;
        TimeText = ((int)position.TotalHours).ToString("00", CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
        var length = _engine.Length;
        Progress = length > TimeSpan.Zero ? Math.Clamp(position / length, 0, 1) : 0;
        Problem = state == PlaybackState.Failed ? _engine.Error ?? CouldNotPlay : "";
        OnPropertyChanged(nameof(AudioTracks));
        OnPropertyChanged(nameof(SubtitleTracks));
        OnPropertyChanged(nameof(Chapters));
    }

    /// <summary>Stops playback and releases the decoder.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Changed -= OnEngineChanged;
        _engine.Stop();
        _engine.Dispose();
    }
}
