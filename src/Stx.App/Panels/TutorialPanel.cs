using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Stx.App.Primitives;
using Stx.Look;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Stx.App.Panels;

/// <summary>The walkthrough video on its own plate. It's only loaded once asked for, and let go of when the plate closes.</summary>
public sealed partial class TutorialPanel : ContentControl
{
    const double Widest = 960, WidthOverHeight = 16.0 / 9;

    readonly string _file, _posterFile;
    readonly MediaPlayerElement _player = new()
    {
        AreTransportControlsEnabled = true,
        AutoPlay = false,
        MaxWidth = Widest,
        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
        Visibility = Visibility.Collapsed,
    };
    readonly StateView _problem = new() { State = ViewState.Error, Visibility = Visibility.Collapsed };
    // With reduce motion on, the poster and a PLAY pad wait here, and the video isn't loaded until it's asked for.
    readonly Grid _waiting = new() { MaxWidth = Widest, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black), Visibility = Visibility.Collapsed };
    readonly Image _poster = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };

    public TutorialPanel(Action close, string? file = null)
    {
        _file = file ?? Path.Combine(AppContext.BaseDirectory, "Assets", "tutorial.mp4");
        _posterFile = Path.Combine(Path.GetDirectoryName(_file)!, "tutorial-poster.png");
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var shut = new Pad { Content = "CLOSE", VerticalAlignment = VerticalAlignment.Center };
        shut.Click += (_, _) => close();
        var title = new Grid { ColumnSpacing = 16 };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(shut, 1);
        title.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Tutorial", VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(shut);

        _player.SizeChanged += (_, e) => _player.Height = e.NewSize.Width / WidthOverHeight;
        _waiting.SizeChanged += (_, e) => _waiting.Height = e.NewSize.Width / WidthOverHeight;
        // Bottom right, clear of the S and the caption on the poster.
        var play = new Pad { Content = "PLAY", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(24) };
        play.Click += (_, _) => Start();
        _waiting.Children.Add(_poster);
        _waiting.Children.Add(play);

        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(title);
        column.Children.Add(_player);
        column.Children.Add(_waiting);
        column.Children.Add(_problem);
        Content = new Faceplate { PanelName = "tutorial", Padding = new Thickness(36, 28, 36, 28), Content = column };
    }

    /// <summary>Plays the video. With reduce motion on it shows the poster and waits for PLAY, and never starts by itself.</summary>
    public void Open()
    {
        if (!File.Exists(_file))
        {
            ShowProblem("THE TUTORIAL VIDEO IS MISSING. REINSTALLING WILL RESTORE IT.");
            return;
        }
        _problem.Visibility = Visibility.Collapsed;
        if (LookState.ReduceMotion)
        {
            if (_poster.Source is null && File.Exists(_posterFile)) _poster.Source = new BitmapImage(new Uri(_posterFile));
            _player.Visibility = Visibility.Collapsed;
            _waiting.Visibility = Visibility.Visible;
            return;
        }
        Start();
    }

    void Start()
    {
        _waiting.Visibility = Visibility.Collapsed;
        _player.Visibility = Visibility.Visible;
        // The element's AutoPlay is handed to the player in SetMediaPlayer, so it has to be on as well.
        _player.AutoPlay = true;
        var player = new MediaPlayer { AutoPlay = true, Source = MediaSource.CreateFromUri(new Uri(_file)) };
        player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() => ShowProblem("THIS PC COULDN'T PLAY THE TUTORIAL VIDEO."));
        _player.SetMediaPlayer(player);
    }

    /// <summary>Stops the video and frees it, so a closed plate holds no decoder.</summary>
    public void Stop()
    {
        var player = _player.MediaPlayer;
        _player.SetMediaPlayer(null);
        player?.Pause();
        player?.Dispose();
    }

    void ShowProblem(string message)
    {
        Stop();
        _player.Visibility = Visibility.Collapsed;
        _waiting.Visibility = Visibility.Collapsed;
        _problem.Message = message;
        _problem.Visibility = Visibility.Visible;
    }
}
