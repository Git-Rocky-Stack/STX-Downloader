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
    readonly PlayerPanel _player;
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
            () => ShowHelp(_tutorial.Visibility == Visibility.Visible ? null : _tutorial), _app.DefaultPlayer) { Visibility = Visibility.Collapsed };
        var first = new FirstRunPanel(_app.FirstRun);
        var truth = new SourceTruthPanel(_app.SourceTruth, _app.ConvertSelected);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(_app.Status, () => settings.Visibility = settings.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        rack.Children.Add(settings);
        rack.Children.Add(_guide);
        rack.Children.Add(_tutorial);
        rack.Children.Add(first);
        rack.Children.Add(new DefaultPlayerOfferPanel(_app.DefaultPlayer));
        rack.Children.Add(new SourcePanel(_app.Source, _app.Clipboard, () => _app.PlayStreamAsync(_app.Source.Link), PickDiscAsync));

        // The deck: QUEUE and LIBRARY on the left; the PLAYER, with SOURCE TRUTH under it reading the same file, on the right.
        var deck = new Grid { ColumnSpacing = 8 };
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new QueuePanel(_app.Queue));
        left.Children.Add(new LibraryPanel(_app.Library, _app.Select, PickShrinkFolderAsync));
        _player = new PlayerPanel(_app.Player, _app.PlayerEngine, _app.Enhance);
        var right = new StackPanel { Spacing = 8 };
        right.Children.Add(_player);
        right.Children.Add(truth);
        Grid.SetColumn(right, 1);
        deck.Children.Add(left);
        deck.Children.Add(right);
        rack.Children.Add(deck);

        // The display glass and rubber stay dark in both modes, so Windows' own theme never leaks in.
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
        var scroller = new ScrollViewer { Content = rack };
        root.Children.Add(scroller);

        // Full screen in place: every other plate folds away and the player's column takes the window. The video
        // never changes parent (spike S1, finding F1).
        _player.FullScreenChanged += (_, on) =>
        {
            foreach (var plate in rack.Children) if (!ReferenceEquals(plate, deck)) plate.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            left.Visibility = truth.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            deck.ColumnDefinitions[0].Width = on ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star);
            deck.ColumnSpacing = on ? 0 : 8;
            rack.Padding = new Thickness(on ? 0 : 8);
            scroller.ChangeView(null, 0, null, disableAnimation: true);
            scroller.VerticalScrollMode = on ? ScrollMode.Disabled : ScrollMode.Enabled;
            scroller.VerticalScrollBarVisibility = on ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
        };
        Content = root;
        if (pointerLight) AddPointerLight(root);

        _app.Activity.Register(() => truth.SweepTimer.IsEnabled || _app.SourceTruth.IsSweeping, truth.SweepTimer.Start, truth.SweepTimer.Stop, keepRunningWhenHidden: false);
        // Hidden in the tray, the player stops decoding pictures; the sound carries on.
        _app.Activity.Register(() => _app.Player.HasMedia, _app.PlayerEngine.ShowVideo, _app.PlayerEngine.HideVideo, keepRunningWhenHidden: false);

        _app.Activity.SetVisible(!StartsHidden);

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _tray = new Tray(handle, ShowFromTray, Quit);
        _clipboard = new ClipboardWatcher(_app.Clipboard);
        ApplyBackgroundChoices(_app.Settings.Current);
        _app.Settings.Changed += (_, current) => ApplyBackgroundChoices(current);
        if (_app.Settings.TakeFirstLaunchGuide()) ShowHelp(_guide);

        // Coming back from Windows' Default Apps page: the LED reads what Windows now does.
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) _app.DefaultPlayer.Refresh(); };
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

    /// <summary>A file from File Explorer or a second launch: it joins the Library and plays, and the window comes forward.</summary>
    public void OpenFromOutside(string path)
    {
        ShowFromTray();
        _app.OpenFromOutside(path);
        _player.StartBringIntoView();
    }

    // SHRINK FOLDER: the copies are written beside the originals, inside the folder picked.
    async Task PickShrinkFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFolderAsync() is { } chosen) await _app.Shrink.QueueAsync(chosen.Path);
    }

    // A drive or a disc folder. Windows' folder picker lists DVD and Blu-ray drives like any other.
    async Task PickDiscAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFolderAsync() is not { } chosen) return;
        _app.PlayDisc(chosen.Path);
        _player.StartBringIntoView();
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
