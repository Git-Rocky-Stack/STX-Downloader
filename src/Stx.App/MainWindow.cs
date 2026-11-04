using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Panels;
using Stx.App.Platform;
using Stx.App.Primitives;
using Stx.Core.Settings;
using Stx.Look;

namespace Stx.App;

/// <summary>The console: everything the app does, on one rack of plates.</summary>
public sealed partial class MainWindow : Window
{
    readonly Composition _app;
    readonly Tray _tray;
    readonly ClipboardWatcher _clipboard;
    readonly GuidePanel _guide;
    readonly TutorialPanel _tutorial;
    bool _quitting;

    public MainWindow(bool startHidden = false, bool pointerLight = false)
    {
        Title = "STX Downloader";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "stx.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 920));
        PaintTitleStrip();
        _app = new Composition(DispatcherQueue);
        StartsHidden = startHidden && _app.Settings.Current.TrayMode;

        _guide = new GuidePanel(() => ShowHelp(null)) { Visibility = Visibility.Collapsed };
        _tutorial = new TutorialPanel(() => ShowHelp(null)) { Visibility = Visibility.Collapsed };
        var settings = new SettingsPanel(_app.Settings, this,
            () => ShowHelp(_guide.Visibility == Visibility.Visible ? null : _guide),
            () => ShowHelp(_tutorial.Visibility == Visibility.Visible ? null : _tutorial)) { Visibility = Visibility.Collapsed };
        var first = new FirstRunPanel(_app.FirstRun);
        var truth = new SourceTruthPanel(_app.SourceTruth, _app.ConvertSelected);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(_app.Status, () => settings.Visibility = settings.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        rack.Children.Add(settings);
        rack.Children.Add(_guide);
        rack.Children.Add(_tutorial);
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
        if (pointerLight) AddPointerLight(root);

        _app.Activity.Register(() => truth.SweepTimer.IsEnabled || _app.SourceTruth.IsSweeping, truth.SweepTimer.Start, truth.SweepTimer.Stop, keepRunningWhenHidden: false);

        _app.Activity.SetVisible(!StartsHidden);

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _tray = new Tray(handle, ShowFromTray, Quit);
        _clipboard = new ClipboardWatcher(_app.Clipboard);
        ApplyBackgroundChoices(_app.Settings.Current);
        _app.Settings.Changed += (_, current) => ApplyBackgroundChoices(current);
        if (_app.Settings.TakeFirstLaunchGuide()) ShowHelp(_guide);

        AppWindow.Closing += OnClosing;
        Closed += (_, _) => Shutdown();
        _ = _app.StartAsync();
    }

    public bool StartsHidden { get; }

    // Black in both modes, running straight into the chassis below it. The S is on the taskbar. Up here its dark plate would vanish.
    void PaintTitleStrip()
    {
        var strip = AppWindow.TitleBar;
        strip.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        strip.BackgroundColor = strip.InactiveBackgroundColor = strip.ButtonBackgroundColor = strip.ButtonInactiveBackgroundColor = TitleStrip.Black;
        strip.ForegroundColor = strip.ButtonForegroundColor = strip.ButtonHoverForegroundColor = strip.ButtonPressedForegroundColor = TitleStrip.Silver;
        strip.InactiveForegroundColor = strip.ButtonInactiveForegroundColor = TitleStrip.SilverDim;
        strip.ButtonHoverBackgroundColor = TitleStrip.ButtonHover;
        strip.ButtonPressedBackgroundColor = TitleStrip.ButtonPressed;
    }

    static class TitleStrip
    {
        public static readonly Windows.UI.Color Black = ColorHelper.FromArgb(255, 8, 9, 10);
        public static readonly Windows.UI.Color Silver = ColorHelper.FromArgb(255, 0xDF, 0xE3, 0xE8);
        public static readonly Windows.UI.Color SilverDim = ColorHelper.FromArgb(255, 0xA4, 0xAB, 0xB3);
        public static readonly Windows.UI.Color ButtonHover = ColorHelper.FromArgb(255, 0x2A, 0x2C, 0x2F);
        public static readonly Windows.UI.Color ButtonPressed = ColorHelper.FromArgb(255, 0x1D, 0x1F, 0x22);
    }

    // Prototype, on only with --pointer-light, until Rocky decides whether it stays.
    static void AddPointerLight(Grid root)
    {
        var overlay = new Canvas { IsHitTestVisible = false };
        root.Children.Add(overlay);
        var light = new PointerLight(overlay);
        root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (LookState.ReduceMotion) { light.Hide(); return; }
            var at = e.GetCurrentPoint(root).Position;
            light.Move((float)at.X, (float)at.Y);
        }), handledEventsToo: true);
        root.PointerExited += (_, _) => light.Hide();
    }

    // One help plate at a time, and a closed tutorial holds no video.
    void ShowHelp(ContentControl? plate)
    {
        _tutorial.Stop();
        _guide.Visibility = ReferenceEquals(plate, _guide) ? Visibility.Visible : Visibility.Collapsed;
        _tutorial.Visibility = ReferenceEquals(plate, _tutorial) ? Visibility.Visible : Visibility.Collapsed;
        if (ReferenceEquals(plate, _tutorial)) _tutorial.Open();
        if (plate is not null) DispatcherQueue.TryEnqueue(() => plate.StartBringIntoView());
    }

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
        _tutorial.Stop();
        _tray.Dispose();
        _clipboard.Dispose();
        _app.Dispose();
    }
}
