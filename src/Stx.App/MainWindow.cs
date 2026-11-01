using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Panels;
using Stx.App.Platform;
using Stx.App.Primitives;
using Stx.Core.Settings;

namespace Stx.App;

/// <summary>The console: everything the app does, on one rack of plates.</summary>
public sealed partial class MainWindow : Window
{
    readonly Composition _app;
    readonly Tray _tray;
    readonly ClipboardWatcher _clipboard;
    bool _quitting;

    public MainWindow(bool startHidden = false)
    {
        Title = "STX Downloader";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 920));
        _app = new Composition(DispatcherQueue);
        StartsHidden = startHidden && _app.Settings.Current.TrayMode;

        var settings = new SettingsPanel(_app.Settings, this) { Visibility = Visibility.Collapsed };
        var first = new FirstRunPanel(_app.FirstRun);
        var truth = new SourceTruthPanel(_app.SourceTruth, _app.ConvertSelected);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(_app.Status, () => settings.Visibility = settings.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        rack.Children.Add(settings);
        rack.Children.Add(first);
        rack.Children.Add(new SourcePanel(_app.Source, _app.Clipboard));

        var deck = new Grid { ColumnSpacing = 8 };
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(truth, 1);
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new QueuePanel(_app.Queue));
        left.Children.Add(new LibraryPanel(_app.Library, _app.Select));
        deck.Children.Add(left);
        deck.Children.Add(truth);
        rack.Children.Add(deck);

        // The display glass and rubber stay dark in both modes, so Windows' own theme never leaks in.
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
        root.Children.Add(new ScrollViewer { Content = rack });
        Content = root;

        _app.Activity.Register(() => truth.SweepTimer.IsEnabled || _app.SourceTruth.IsSweeping, truth.SweepTimer.Start, truth.SweepTimer.Stop, keepRunningWhenHidden: false);

        _app.Activity.SetVisible(!StartsHidden);

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _tray = new Tray(handle, ShowFromTray, Quit);
        _clipboard = new ClipboardWatcher(_app.Clipboard);
        ApplyBackgroundChoices(_app.Settings.Current);
        _app.Settings.Changed += (_, current) => ApplyBackgroundChoices(current);

        AppWindow.Closing += OnClosing;
        Closed += (_, _) => Shutdown();
        _ = _app.StartAsync();
    }

    public bool StartsHidden { get; }

    /// <summary>Brings the window back, from the tray or from a second launch.</summary>
    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        _app.Activity.SetVisible(true);
    }

    void ApplyBackgroundChoices(AppSettings settings)
    {
        _tray.SetVisible(settings.TrayMode);
        _clipboard.SetEnabled(settings.ClipboardDetect);
    }

    // With tray mode on, closing the window keeps downloads going in the background, and it does no other work there.
    void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_quitting || !_app.Settings.Current.TrayMode) return;
        args.Cancel = true;
        AppWindow.Hide();
        _app.Activity.SetVisible(false);
    }

    void Quit()
    {
        _quitting = true;
        Close();
    }

    void Shutdown()
    {
        _tray.Dispose();
        _clipboard.Dispose();
        _app.Dispose();
    }
}
