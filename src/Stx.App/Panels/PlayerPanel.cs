using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Core.Playback;
using Stx.Look;
using Stx.Playback;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>
/// The PLAYER plate: the picture, its transport pads, the position, the time and the volume. Full screen happens in
/// place: the picture and its control strip fill the window and the plate's metal steps aside, but nothing moves to
/// another parent, so the video never restarts (spike S1, finding F1).
/// </summary>
public sealed partial class PlayerPanel : ContentControl
{
    const double WidthOverHeight = 16.0 / 9;
    static readonly IReadOnlyList<string> VolumeSteps = [.. Enumerable.Range(0, 11).Select(i => (i * 10).ToString(System.Globalization.CultureInfo.InvariantCulture))];

    readonly PlayerViewModel _player;
    readonly Faceplate _plate;
    readonly Grid _stage = new();
    readonly Grid _screen = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
    readonly Grid _strip = new() { ColumnSpacing = 12, RowSpacing = 8, Padding = new Thickness(0, 12, 0, 0) };
    readonly Grid _titleRow = new() { ColumnSpacing = 16, Margin = new Thickness(0, 0, 0, 12) };
    readonly StackPanel _pickers = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    readonly Display _audioOnly = new() { Tone = DisplayTone.Cyan, Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    readonly GlowText _audioTitle = new() { Tone = DisplayTone.Cyan, TextSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly GlowText _nowPlaying = new() { Tone = DisplayTone.Cyan, VerticalAlignment = VerticalAlignment.Center };
    readonly StateView _problem = new() { State = ViewState.Error, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    readonly Pad _previous = new() { Content = "PREV" }, _playPause = new() { Content = "PLAY" }, _next = new() { Content = "NEXT" },
                 _stop = new() { Content = "STOP" }, _full = new() { Content = "FULL" };
    readonly SeekBar _seek = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly SevenSegmentReadout _time = new() { Digits = 8, VerticalAlignment = VerticalAlignment.Center };
    readonly Rotary _volume = new() { Label = "Volume", Items = VolumeSteps };
    readonly Grid _pads = new() { ColumnSpacing = 8 };
    readonly Pad[] _transport;
    readonly StackPanel _volumeDial;
    Rotary? _audioPicker, _subtitlePicker, _chapterPicker;
    bool _wasFullScreen;

    public PlayerPanel(PlayerViewModel player, LibVlcEngine engine)
    {
        _player = player;
        _transport = [_previous, _playPause, _next, _stop, _full];
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        Surface = new VideoSurface();
        Surface.Attach(engine);
        Surface.DoubleTapped += (_, _) => _player.FullScreenCommand.Execute(null);
        AutomationProperties.SetName(Surface, "Picture");

        // The title on its own row; folded away in full screen.
        _titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        _titleRow.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Player", VerticalAlignment = VerticalAlignment.Center });
        // The title on glass, as every plate shows its text, so it reads on the light plate as well as the dark one.
        var titleGlass = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(12, 4, 12, 4), Content = _nowPlaying };
        Grid.SetColumn(titleGlass, 1);
        _titleRow.Children.Add(titleGlass);

        // The picture, with the music display and the problem line over it.
        _audioOnly.Content = _audioTitle;
        _screen.Children.Add(Surface);
        _screen.Children.Add(_audioOnly);
        _screen.Children.Add(_problem);
        _screen.SizeChanged += (_, e) => { if (!_player.IsFullScreen) _screen.Height = e.NewSize.Width / WidthOverHeight; };

        // On the plate: three rows, so it fits the rack's right column (the position across the full width; the time,
        // the volume and any track pickers the file needs; then the five pads sharing the width). In full screen: one
        // row over the bottom of the picture, so it covers as little of it as it can (Layout).
        for (int i = 0; i < _transport.Length; i++)
        {
            _pads.ColumnDefinitions.Add(new ColumnDefinition());
            _transport[i].MinWidth = 44;
            _transport[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(_transport[i], i);
            _pads.Children.Add(_transport[i]);
        }
        _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _strip.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) _strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 3; i++) _strip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _volumeDial = _volume.Captioned();
        _time.HorizontalAlignment = HorizontalAlignment.Left;
        _strip.Children.Add(_pads);
        _strip.Children.Add(_seek);
        _strip.Children.Add(_time);
        _strip.Children.Add(_volumeDial);
        _strip.Children.Add(_pickers);
        Layout(fullScreen: false);

        _stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_screen, 1);
        Grid.SetRow(_strip, 2);
        _stage.Children.Add(_titleRow);
        _stage.Children.Add(_screen);
        _stage.Children.Add(_strip);

        _previous.Click += (_, _) => _player.PreviousCommand.Execute(null);
        _playPause.Click += (_, _) => _player.PlayPauseCommand.Execute(null);
        _next.Click += (_, _) => _player.NextCommand.Execute(null);
        _stop.Click += (_, _) => _player.StopCommand.Execute(null);
        _full.Click += (_, _) => _player.FullScreenCommand.Execute(null);
        _seek.Seeked += (_, fraction) => _player.SeekCommand.Execute(fraction);
        _volume.SelectedIndex = Math.Clamp(_player.Volume / 10, 0, 10);
        _volume.SelectionChanged += (_, index) => { if (index >= 0) _player.Volume = index * 10; };

        // Esc leaves full screen.
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, e) => { if (_player.IsFullScreen) { _player.FullScreenCommand.Execute(null); e.Handled = true; } };
        KeyboardAccelerators.Add(escape);

        _plate = new Faceplate { PanelName = "player", Padding = new Thickness(36, 28, 36, 28), Content = _stage };
        Content = _plate;
        _player.PropertyChanged += OnPlayerChanged;
        Unloaded += (_, _) => _player.PropertyChanged -= OnPlayerChanged;
        Render();
    }

    /// <summary>The surface LibVLC draws into.</summary>
    public VideoSurface Surface { get; }

    /// <summary>Raised when full screen starts or ends. The window owner switches its presenter and folds the other plates.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    void OnPlayerChanged(object? sender, PropertyChangedEventArgs e) => Render();

    void Render()
    {
        _playPause.Content = _player.IsPlaying ? "PAUSE" : "PLAY";
        _seek.Fraction = _player.Progress;
        _time.Text = _player.TimeText;
        int volumeStep = Math.Clamp((int)Math.Round(_player.Volume / 10.0), 0, 10);
        if (_volume.SelectedIndex != volumeStep) _volume.SelectedIndex = volumeStep;
        _nowPlaying.Text = _player.Title.ToUpperInvariant();
        _audioTitle.Text = _player.Title.ToUpperInvariant();
        _audioOnly.Visibility = _player.IsAudioOnly ? Visibility.Visible : Visibility.Collapsed;
        _problem.Message = _player.Problem;
        _problem.Visibility = _player.Problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _audioPicker = Picker(_audioPicker, "Audio", _player.AudioTracks, _player.SelectAudio);
        _subtitlePicker = Picker(_subtitlePicker, "Subtitles", _player.SubtitleTracks, _player.SelectSubtitle);
        _chapterPicker = Picker(_chapterPicker, "Chapter", _player.Chapters, _player.SelectChapter);
        if (_player.IsFullScreen != _wasFullScreen) SetFullScreen(_player.IsFullScreen);
    }

    // A knob appears only when the file offers more than one choice.
    Rotary? Picker(Rotary? current, string label, IReadOnlyList<MediaTrack> tracks, Action<int> select)
    {
        var names = tracks.Select(t => t.Name.ToUpperInvariant()).ToList();
        if (current is not null && current.Items.SequenceEqual(names)) return current;
        if (current is not null) _pickers.Children.Remove((UIElement)current.Parent);
        if (tracks.Count < 2) return null;
        var picker = new Rotary { Label = label, Items = names };
        picker.SelectionChanged += (_, index) => { if (index >= 0 && index < tracks.Count) select(tracks[index].Id); };
        _pickers.Children.Add(picker.Captioned());
        return picker;
    }

    // Where each part of the strip sits: (row, column, column span).
    void Layout(bool fullScreen)
    {
        static void Place(FrameworkElement e, int row, int column, int span) { Grid.SetRow(e, row); Grid.SetColumn(e, column); Grid.SetColumnSpan(e, span); }
        if (fullScreen)
        {
            Place(_pads, 0, 0, 1);
            Place(_seek, 0, 1, 1);
            Place(_time, 0, 2, 1);
            Place(_volumeDial, 0, 3, 1);
            Place(_pickers, 0, 4, 1);
            // Narrower pads in full screen leave the position bar room; still well over 44 px each.
            _pads.Width = _transport.Length * 80 + (_transport.Length - 1) * 8;
            _pads.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            Place(_seek, 0, 0, 5);
            Place(_time, 1, 0, 2);
            Place(_volumeDial, 1, 3, 1);
            Place(_pickers, 1, 4, 1);
            Place(_pads, 2, 0, 5);
            _pads.Width = double.NaN;
            _pads.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    void SetFullScreen(bool on)
    {
        _wasFullScreen = on;
        _plate.Bare = on;
        Layout(on);
        _plate.Padding = on ? new Thickness(0) : new Thickness(36, 28, 36, 28);
        _titleRow.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        // In full screen the strip sits over the bottom of the picture, on a dark band, instead of under it.
        Grid.SetRow(_strip, on ? 1 : 2);
        _strip.VerticalAlignment = on ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _strip.Padding = on ? new Thickness(24, 12, 24, 12) : new Thickness(0, 12, 0, 0);
        _strip.Background = on ? new SolidColorBrush(Windows.UI.Color.FromArgb(200, 8, 9, 10)) : null;
        _full.Content = on ? "EXIT" : "FULL";
        if (on && XamlRoot is { } root) _screen.Height = root.Size.Height;
        else _screen.Height = _screen.ActualWidth / WidthOverHeight;
        if (on && XamlRoot is { } r) r.Changed += FitScreen;
        else if (XamlRoot is { } r2) r2.Changed -= FitScreen;
        FullScreenChanged?.Invoke(this, on);
    }

    void FitScreen(XamlRoot sender, XamlRootChangedEventArgs e)
    {
        if (_player.IsFullScreen) _screen.Height = sender.Size.Height;
    }
}
