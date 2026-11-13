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
    readonly SettingsPanel _settings;
    readonly TourOverlay _tour;
    // Where a running tutorial came from (the Tutorials plate, or the guide's SHOW ME), to go back to when it ends,
    // and whether it opened Settings itself, so it can fold Settings away again.
    ContentControl? _tourFrom;
    bool _tourOpenedSettings;
    bool _quitting;

    public MainWindow(bool startHidden = false, bool pointerLight = false)
    {
        Title = "STX Downloader";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "stx.ico"));
        Platform.WindowSize.Set(AppWindow, 1360, 920);
        TitleStrip.Paint(AppWindow);
        _app = new Composition(DispatcherQueue);
        StartsHidden = startHidden && _app.Settings.Current.TrayMode;

        _guide = new GuidePanel(() => ShowHelp(null), tour => StartTour(tour, _guide)) { Visibility = Visibility.Collapsed };
        _tutorial = new TutorialPanel(() => ShowHelp(null), tour => StartTour(tour, _tutorial), _app.Settings.IsTutorialDone) { Visibility = Visibility.Collapsed };
        var settings = _settings = new SettingsPanel(_app.Settings, this,
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
        // Each column's last plate stretches (a star row), and the deck gives both columns the taller one's height, so
        // their bottoms meet: no dead space under either, full or empty (review 2026-11-12).
        var deck = new DeckPanel { LeftWeight = 1.2, RightWeight = 1, Spacing = 8 };
        var left = DeckPanel.Column(new QueuePanel(_app.Queue), new LibraryPanel(_app.Library, _app.Select, PickShrinkFolderAsync));
        _player = new PlayerPanel(_app.Player, _app.PlayerEngine, _app.Enhance);
        var right = DeckPanel.Column(_player, truth);
        deck.Children.Add(left);
        deck.Children.Add(right);
        rack.Children.Add(deck);

        // The display glass and rubber stay dark in both modes, so Windows' own theme never leaks in.
        var root = new Grid { RequestedTheme = ElementTheme.Dark, Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
        var scroller = new ScrollViewer { Content = rack };
        root.Children.Add(scroller);
        // Tutorials run over the whole window, on the real controls.
        _tour = new TourOverlay(root, scroller, RevealForTour, EndTour);
        root.Children.Add(_tour);

        // Full screen in place: every other plate folds away and the player's column takes the window. The video
        // never changes parent (spike S1, finding F1).
        _player.FullScreenChanged += (_, on) =>
        {
            // FULL pressed inside a tutorial's spotlight: the rack it was showing folds away, so the tour ends there.
            if (on) _tour.Stop();
            foreach (var plate in rack.Children) if (!ReferenceEquals(plate, deck)) plate.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            // A collapsed left column gives the player's column the deck's whole width.
            left.Visibility = truth.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
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
        App.Fatal += () => _tray.SetVisible(false);
        _clipboard = new ClipboardWatcher(_app.Clipboard);
        ApplyBackgroundChoices(_app.Settings.Current);
        _app.Settings.Changed += (_, current) => ApplyBackgroundChoices(current);
        if (_app.Settings.TakeFirstLaunchGuide()) ShowHelp(_guide);

        // Coming back from Windows' Default Apps page: the LED reads what Windows now does.
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) _app.DefaultPlayer.Refresh(); };
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => Shutdown();
        _ = Stx.Core.Logging.ErrorLog.Observe(_app.StartAsync(), "startup");
    }

    public bool StartsHidden { get; }



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

    // One help plate at a time. The Tutorials plate reads which chapters are finished each time it opens.
    void ShowHelp(ContentControl? plate)
    {
        _guide.Visibility = ReferenceEquals(plate, _guide) ? Visibility.Visible : Visibility.Collapsed;
        _tutorial.Visibility = ReferenceEquals(plate, _tutorial) ? Visibility.Visible : Visibility.Collapsed;
        if (ReferenceEquals(plate, _tutorial)) _tutorial.Refresh();
        if (plate is not null) DispatcherQueue.TryEnqueue(() => plate.StartBringIntoView());
    }

    // A tutorial runs on the console as it normally looks, so the help plate that started it folds away until it ends.
    void StartTour(Stx.ViewModels.Tour tour, ContentControl? from)
    {
        _tourFrom = from;
        _tourOpenedSettings = false;
        ShowHelp(null);
        _tour.Start(tour);
    }

    // A step on a plate that's folded away shows it: only Settings folds away while the console runs.
    bool RevealForTour(string plate)
    {
        if (plate != "settings" || _settings.Visibility == Visibility.Visible) return false;
        _settings.Visibility = Visibility.Visible;
        _tourOpenedSettings = true;
        return true;
    }

    void EndTour(Stx.ViewModels.Tour tour, bool finished)
    {
        if (finished) _app.Settings.MarkTutorialDone(tour.Id);
        if (_tourOpenedSettings) _settings.Visibility = Visibility.Collapsed;
        _tourOpenedSettings = false;
        var from = _tourFrom;
        _tourFrom = null;
        // Back where it started, unless the window is in full screen now.
        if (from is not null && !_app.Player.IsFullScreen) ShowHelp(from);
    }

    /// <summary>A file from File Explorer or a second launch: it joins the Library and plays, and the window comes forward.</summary>
    public void OpenFromOutside(string path)
    {
        ShowFromTray();
        _ = Stx.Core.Logging.ErrorLog.Observe(_app.OpenFromOutsideAsync(path), "open from outside");
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

    /// <summary>Something failed on the UI thread and was caught at the top: the status line says so.</summary>
    public void ReportRecovered() => _app.Status.Report(new Stx.Tools.ToolStatus("STX", "recovered from a problem; details are in the error log"));

    /// <summary>Brings the window back, from the tray or from a second launch.</summary>
    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        _app.Activity.SetVisible(true);
    }

    void ApplyBackgroundChoices(AppSettings settings)
    {
        // A tray icon Windows wouldn't add (the taskbar not up yet at sign-in) must not leave a hidden window with no
        // way back to it: the window is shown, and the icon is added when the taskbar announces itself.
        if (!_tray.SetVisible(settings.TrayMode) && settings.TrayMode && !AppWindow.IsVisible) ShowFromTray();
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
        _tour.Detach();
        _tray.Dispose();
        _clipboard.Dispose();
        _app.Dispose();
    }
}
