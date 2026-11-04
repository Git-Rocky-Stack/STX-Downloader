using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Look;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Stx.App.Panels;

/// <summary>The walkthrough video on its own plate. It's only loaded once asked for, and let go of when the plate closes.</summary>
public sealed partial class TutorialPanel : ContentControl
{
    const double Widest = 960, WidthOverHeight = 16.0 / 9;

    readonly string _file;
    readonly MediaPlayerElement _player = new()
    {
        AreTransportControlsEnabled = true,
        AutoPlay = false,
        MaxWidth = Widest,
        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
        Visibility = Visibility.Collapsed,
    };
    readonly StateView _problem = new() { State = ViewState.Error, Visibility = Visibility.Collapsed };

    public TutorialPanel(Action close, string? file = null)
    {
        _file = file ?? Path.Combine(AppContext.BaseDirectory, "Assets", "tutorial.mp4");
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

        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(title);
        column.Children.Add(_player);
        column.Children.Add(_problem);
        Content = new Faceplate { PanelName = "tutorial", Padding = new Thickness(36, 28, 36, 28), Content = column };
    }

    /// <summary>Loads the video. With reduce motion on it waits for the play button, and never starts by itself.</summary>
    public void Open()
    {
        if (!File.Exists(_file))
        {
            ShowProblem("THE TUTORIAL VIDEO IS MISSING. REINSTALLING WILL RESTORE IT.");
            return;
        }
        _problem.Visibility = Visibility.Collapsed;
        _player.Visibility = Visibility.Visible;
        // The element's AutoPlay is handed to the player in SetMediaPlayer, so both follow reduce motion.
        _player.AutoPlay = !LookState.ReduceMotion;
        var player = new MediaPlayer { AutoPlay = _player.AutoPlay, Source = MediaSource.CreateFromUri(new Uri(_file)) };
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
        _problem.Message = message;
        _problem.Visibility = Visibility.Visible;
    }
}
