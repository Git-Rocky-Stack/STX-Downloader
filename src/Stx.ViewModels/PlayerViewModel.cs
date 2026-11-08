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
    IPlayOrder _order = new ListOrder([]);
    // What's playing, or was last: what PLAY plays again once it has stopped, and where PREV and NEXT step from.
    PlayItem? _current;
    // One step of PREV or NEXT at a time: a second press waits for the first, so two presses step twice.
    readonly SemaphoreSlim _stepping = new(1, 1);
    bool _disposed;
    // Whether the file playing has a picture, read while it plays. LibVLC lists no tracks once a file has ended, so
    // the answer is kept: a finished song stays music, and a finished film isn't taken for one.
    bool? _hasPicture;

    public PlayerViewModel(IPlaybackEngine engine, Action<Action> onUi)
    {
        _engine = engine;
        _onUi = onUi;
        _engine.Changed += OnEngineChanged;
    }

    string _title = "", _timeText = "00:00:00", _problem = "", _audioReadout = "";
    bool _hasMedia, _isPlaying, _isAudioOnly, _isFullScreen, _volumeFixed;
    double _progress;

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public bool HasMedia { get => _hasMedia; private set => SetProperty(ref _hasMedia, value); }
    public bool IsPlaying { get => _isPlaying; private set => SetProperty(ref _isPlaying, value); }
    public bool IsAudioOnly { get => _isAudioOnly; private set => SetProperty(ref _isAudioOnly, value); }
    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Problem { get => _problem; private set => SetProperty(ref _problem, value); }
    public bool IsFullScreen { get => _isFullScreen; set => SetProperty(ref _isFullScreen, value); }

    /// <summary>What reaches the sound device while BIT-PERFECT is on ("24/96 BIT-PERFECT", or why it's shared). Empty otherwise.</summary>
    public string AudioReadout { get => _audioReadout; private set => SetProperty(ref _audioReadout, value); }

    /// <summary>True while a bit-perfect stream plays: it goes out at full scale, so VOLUME can't change it.</summary>
    public bool VolumeFixed { get => _volumeFixed; private set => SetProperty(ref _volumeFixed, value); }

    public int Volume
    {
        get => _engine.Volume;
        set { if (_engine.Volume == value) return; _engine.Volume = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<MediaTrack> AudioTracks => _engine.AudioTracks;
    public IReadOnlyList<MediaTrack> SubtitleTracks => _engine.SubtitleTracks;
    public IReadOnlyList<MediaTrack> Chapters => _engine.Chapters;

    /// <summary>Plays <paramref name="path"/>, replacing whatever was playing. The list is what PREV and NEXT walk.</summary>
    public void Play(string path, string title, IReadOnlyList<(string Path, string Title)> list) => Play(path, title, new ListOrder(list));

    /// <summary>Plays <paramref name="path"/>, replacing whatever was playing. PREV and NEXT walk <paramref name="order"/> from it.</summary>
    public void Play(string path, string title, IPlayOrder order)
    {
        _order = order;
        PlayItem(new PlayItem(path, title));
        Refresh();
    }

    [RelayCommand]
    void PlayPause()
    {
        if (_engine.State == PlaybackState.Idle && _current is { } current) PlayItem(current);
        else _engine.TogglePause();
        Refresh();
    }

    [RelayCommand]
    void Stop()
    {
        _engine.Stop();
        Refresh();
    }

    /// <summary>Back to the start of the file, or once it's barely begun, to the file before. The order is read off the UI thread.</summary>
    [RelayCommand]
    async Task Previous()
    {
        await _stepping.WaitAsync();
        try
        {
            var before = _current is { } current && _engine.Position <= RestartAfter ? await _order.BeforeAsync(current.Path) : null;
            if (_disposed) return;
            if (before is null) _engine.Seek(TimeSpan.Zero);
            else PlayItem(before);
            Refresh();
        }
        finally { _stepping.Release(); }
    }

    /// <summary>The file after, or a stop at the end. The order is read off the UI thread.</summary>
    [RelayCommand]
    async Task Next()
    {
        await _stepping.WaitAsync();
        try
        {
            var after = _current is { } current ? await _order.AfterAsync(current.Path) : null;
            if (_disposed) return;
            if (after is null) _engine.Stop();
            else PlayItem(after);
            Refresh();
        }
        finally { _stepping.Release(); }
    }

    [RelayCommand]
    void FullScreen() => IsFullScreen = !IsFullScreen;

    /// <summary>Jumps to a fraction of the way through, 0 to 1.</summary>
    [RelayCommand]
    void Seek(double fraction) => _engine.Seek(_engine.Length * Math.Clamp(fraction, 0, 1));

    public void SelectAudio(int id) => _engine.SelectAudio(id);
    public void SelectSubtitle(int id) => _engine.SelectSubtitle(id);
    public void SelectChapter(int id) => _engine.SelectChapter(id);

    void PlayItem(PlayItem item)
    {
        _current = item;
        _hasPicture = null;
        Title = item.Title;
        _engine.Open(item.Path);
    }

    void OnEngineChanged(object? sender, EventArgs e) => _onUi(Refresh);

    void Refresh()
    {
        if (_disposed) return;
        var state = _engine.State;
        HasMedia = state is not PlaybackState.Idle;
        IsPlaying = state is PlaybackState.Playing;
        if (state is PlaybackState.Playing or PlaybackState.Paused) _hasPicture = _engine.HasVideo;
        IsAudioOnly = HasMedia && _hasPicture == false;
        var position = _engine.Position;
        TimeText = ((int)position.TotalHours).ToString("00", CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
        var length = _engine.Length;
        Progress = length > TimeSpan.Zero ? Math.Clamp(position / length, 0, 1) : 0;
        Problem = state == PlaybackState.Failed ? _engine.Error ?? CouldNotPlay : "";
        AudioReadout = HasMedia ? _engine.AudioReadout : "";
        VolumeFixed = HasMedia && _engine.VolumeFixed;
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
