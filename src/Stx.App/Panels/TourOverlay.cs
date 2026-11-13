using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace Stx.App.Panels;

/// <summary>
/// A tutorial running on the live console. The window dims, a spotlight opens over the step's real control (or its plate,
/// when the control isn't showing), and a card beside it explains the step, with BACK, NEXT (FINISH on the last step)
/// and END TOUR. The spotlight is a hole in the dim layer, so the lit control still works and can be tried; the dimmed
/// rest can't be pressed by accident. It follows its control as the rack scrolls or the window resizes, gliding from one
/// step to the next unless REDUCE MOTION is on.
/// </summary>
public sealed partial class TourOverlay : Grid
{
    // On the 8-point grid: the card keeps 16 from the window's edge and from the spotlight, which opens 8 around its control.
    const double Gap = 16, Edge = 16, HolePad = 8, CardWidth = 420, Radius = 10;
    const double Glide = 0.28; // of the remaining distance, per frame: about 200 ms to settle at 60 fps

    static readonly SolidColorBrush Shade = new(ColorHelper.FromArgb(0xB8, 0x03, 0x04, 0x05));
    static readonly SolidColorBrush CardFill = new(ColorHelper.FromArgb(0xF5, 0x14, 0x17, 0x1A));
    static readonly SolidColorBrush CardEdge = new(ColorHelper.FromArgb(0x55, 0x7F, 0xD4, 0xFF));
    static readonly SolidColorBrush Spot = LookBrushes.From("#7FD4FF");
    static readonly SolidColorBrush Bright = LookBrushes.From("#F2F5F8");
    static readonly SolidColorBrush Body = LookBrushes.From("#C9D0D7");
    static readonly SolidColorBrush Faint = LookBrushes.From("#8E98A3");

    readonly Func<string, bool> _reveal;
    readonly Action<Tour, bool> _ended;
    readonly RectangleGeometry _all = new();
    readonly GeometryGroup _layers = new() { FillRule = FillRule.EvenOdd };
    readonly Canvas _canvas = new();
    readonly Border _ring = new() { BorderBrush = Spot, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(Radius), IsHitTestVisible = false };
    readonly Border _halo = new() { BorderBrush = Spot, BorderThickness = new Thickness(6), CornerRadius = new CornerRadius(Radius + 4), Opacity = 0.18, IsHitTestVisible = false };
    readonly Border _card;
    readonly TextBlock _chapter = Text(12, Faint, semibold: true), _counter = Text(12, Spot, semibold: true), _title = Text(20, Bright, semibold: true), _body = Text(14, Body);
    readonly Bargraph _progress = new() { Zones = BargraphZones.Progress, Height = 16 };
    readonly Pad _back = new() { Content = "BACK" }, _forward = new() { Content = "NEXT", Lit = true, LitTone = PadTone.Blue }, _end = new() { Content = "END TOUR" };

    Tour? _tour;
    int _index;
    FrameworkElement? _target;
    Rect _shown = Rect.Empty, _aim = Rect.Empty;
    bool _ticking;
    int _still;

    /// <param name="stage">The window's root, which this overlay covers and whose keys it listens to.</param>
    /// <param name="scroller">The rack's scroller: the spotlight follows its control as it scrolls.</param>
    /// <param name="reveal">Shows a plate a step needs that's folded away (Settings), by its key.</param>
    /// <param name="ended">The tour, and whether it was finished to its last step.</param>
    public TourOverlay(UIElement stage, ScrollViewer scroller, Func<string, bool> reveal, Action<Tour, bool> ended)
    {
        _reveal = reveal;
        _ended = ended;
        Visibility = Visibility.Collapsed;
        // The dim layer: the whole window less the spotlight. Even-odd leaves the hole unfilled, and an unfilled area
        // isn't hit-tested, so a click there reaches the control under it.
        _layers.Children.Add(_all);
        _layers.Children.Add(RoundedRect(new Rect(0, 0, 0, 0)));
        var dim = new Microsoft.UI.Xaml.Shapes.Path { Fill = Shade, Data = _layers };
        AutomationProperties.SetAccessibilityView(dim, AccessibilityView.Raw);

        _back.Click += (_, _) => Step(-1);
        _forward.Click += (_, _) => Step(+1);
        _end.Click += (_, _) => Stop(finished: false);
        AutomationProperties.SetLiveSetting(_body, AutomationLiveSetting.Assertive);

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_counter, 1);
        head.Children.Add(_chapter);
        head.Children.Add(_counter);
        var pads = new PadRow();
        pads.Children.Add(_back);
        pads.Children.Add(_forward);
        pads.Children.Add(_end);
        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(head);
        column.Children.Add(_progress);
        column.Children.Add(_title);
        column.Children.Add(_body);
        column.Children.Add(pads);
        _card = new Border
        {
            Width = CardWidth,
            Background = CardFill,
            BorderBrush = CardEdge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Padding = new Thickness(24, 20, 24, 20),
            Child = column,
        };
        AutomationProperties.SetName(_card, "Tutorial");
        AutomationProperties.SetLandmarkType(_card, AutomationLandmarkType.Custom);

        _canvas.Children.Add(_halo);
        _canvas.Children.Add(_ring);
        _canvas.Children.Add(_card);
        Children.Add(dim);
        Children.Add(_canvas);

        SizeChanged += (_, _) => Kick();
        scroller.ViewChanging += (_, _) => Kick();
        scroller.ViewChanged += (_, _) => Kick();
        stage.AddHandler(KeyDownEvent, new KeyEventHandler(OnKey), handledEventsToo: true);
        stage.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, _) => Kick()), handledEventsToo: true);
        LayoutUpdated += (_, _) => { if (_tour is not null && !_ticking && _target is not null && Aim() != _aim) Kick(); };
    }

    public bool IsRunning => _tour is not null;

    /// <summary>Starts a chapter at its first step.</summary>
    public void Start(Tour tour)
    {
        _tour = tour;
        _shown = _aim = Rect.Empty;
        Visibility = Visibility.Visible;
        _card.OpacityTransition = LookState.ReduceMotion ? null : new ScalarTransition { Duration = TimeSpan.FromMilliseconds(200) };
        Show(0);
    }

    /// <summary>Ends the tour where it is. Finished only when NEXT was pressed on the last step.</summary>
    public void Stop(bool finished = false)
    {
        if (_tour is not { } tour) return;
        _tour = null;
        _target = null;
        StopTicking();
        Visibility = Visibility.Collapsed;
        _ended(tour, finished);
    }

    /// <summary>Jumps to a step, as the gallery holds one for a picture.</summary>
    public void GoTo(int index)
    {
        if (_tour is { } tour && index >= 0 && index < tour.Steps.Count) Show(index);
    }

    /// <summary>The window is closing: lets go of the frame clock without ending the tour through its callback, which
    /// would rearrange plates on a window that's going away.</summary>
    public void Detach()
    {
        StopTicking();
        _tour = null;
        _target = null;
    }

    void Step(int by)
    {
        if (_tour is not { } tour) return;
        int next = _index + by;
        if (next >= tour.Steps.Count) { Stop(finished: true); return; }
        if (next >= 0) Show(next);
    }

    void Show(int index)
    {
        if (_tour is not { } tour) return;
        _index = index;
        var step = tour.Steps[index];
        bool last = index == tour.Steps.Count - 1;

        _chapter.Text = tour.Title.ToUpperInvariant();
        _counter.Text = $"STEP {index + 1} OF {tour.Steps.Count}";
        _progress.Value = (index + 1) * 100.0 / tour.Steps.Count;
        _title.Text = step.Title;
        Fill(_body, step.Line, step.Marked);
        _back.IsEnabled = index > 0;
        if (last) _forward.Content = "FINISH";
        else _forward.Content = "NEXT";
        AutomationProperties.SetName(_forward, last ? "Finish this tutorial" : "Next step");
        _card.Opacity = 0;

        // A plate folded away (Settings) is shown first; the control is found once that layout has run.
        _reveal(step.Plate);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!ReferenceEquals(_tour, tour) || _index != index) return;
            _target = TourTargets.Find(step.Target) ?? TourTargets.Find(step.Plate);
            _target?.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = !LookState.ReduceMotion, VerticalAlignmentRatio = 0.35 });
            _aim = Rect.Empty;
            Kick();
            _card.Opacity = 1;
            _forward.Focus(FocusState.Programmatic);
            if ((FrameworkElementAutomationPeer.FromElement(_body) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_body)) is { } peer)
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }

    // Esc ends the tour from anywhere; the arrow keys step it while the card has focus, so a text box keeps its own.
    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (_tour is null) return;
        if (e.Key == VirtualKey.Escape)
        {
            Stop(finished: false);
            e.Handled = true;
            return;
        }
        if (FocusManager.GetFocusedElement(XamlRoot) is not Pad pad || !(ReferenceEquals(pad, _back) || ReferenceEquals(pad, _forward) || ReferenceEquals(pad, _end))) return;
        if (e.Key == VirtualKey.Right) { Step(+1); e.Handled = true; }
        else if (e.Key == VirtualKey.Left && _index > 0) { Step(-1); e.Handled = true; }
    }

    // Each frame while something moves: where the control is now, and the spotlight a step closer to it. It stops
    // listening for frames once nothing has moved for a third of a second.
    void Kick()
    {
        if (_tour is null) return;
        _still = 0;
        if (_ticking) return;
        _ticking = true;
        CompositionTarget.Rendering += OnFrame;
    }

    void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    void OnFrame(object? sender, object e)
    {
        if (_tour is null || ActualWidth < 1 || ActualHeight < 1) { StopTicking(); return; }
        var aim = Aim();
        if (aim != _aim)
        {
            _aim = aim;
            _still = 0;
            PlaceCard(aim);
        }
        var next = _shown.IsEmpty || aim.IsEmpty || LookState.ReduceMotion ? aim : Toward(_shown, aim);
        if (next != _shown) Spotlight(next);
        else if (++_still > 20) StopTicking();
    }

    // The control's bounds on this overlay, opened by HolePad and kept on the window. Empty when nothing is lit.
    Rect Aim()
    {
        if (_target is not { } target || !TourTargets.IsShown(target)) return Rect.Empty;
        Rect bounds;
        try { bounds = target.TransformToVisual(this).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight)); }
        catch (ArgumentException) { return Rect.Empty; } // left the tree between frames
        double left = Math.Max(0, bounds.X - HolePad), top = Math.Max(0, bounds.Y - HolePad);
        double right = Math.Min(ActualWidth, bounds.Right + HolePad), bottom = Math.Min(ActualHeight, bounds.Bottom + HolePad);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : Rect.Empty;
    }

    static Rect Toward(Rect from, Rect to)
    {
        static double Ease(double a, double b) => Math.Abs(b - a) < 0.5 ? b : a + (b - a) * Glide;
        return new Rect(Ease(from.X, to.X), Ease(from.Y, to.Y), Ease(from.Width, to.Width), Ease(from.Height, to.Height));
    }

    void Spotlight(Rect hole)
    {
        _shown = hole;
        _all.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
        _layers.Children[1] = RoundedRect(hole.IsEmpty ? new Rect(0, 0, 0, 0) : hole);
        var visible = hole.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        _ring.Visibility = _halo.Visibility = visible;
        if (hole.IsEmpty) return;
        Put(_ring, hole.X, hole.Y, hole.Width, hole.Height);
        Put(_halo, hole.X - 4, hole.Y - 4, hole.Width + 8, hole.Height + 8);
    }

    // WinUI's RectangleGeometry has no corner radius, so the spotlight's rounded hole is drawn as a path: four sides and
    // four quarter-circle corners, the radius shrinking for a hole too small to hold it.
    static PathGeometry RoundedRect(Rect r)
    {
        double radius = Math.Min(Radius, Math.Min(r.Width, r.Height) / 2);
        var figure = new PathFigure { StartPoint = new Point(r.Left + radius, r.Top), IsClosed = true, IsFilled = true };
        void Line(double x, double y) => figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
        void Corner(double x, double y) => figure.Segments.Add(new ArcSegment { Point = new Point(x, y), Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise });
        Line(r.Right - radius, r.Top);
        Corner(r.Right, r.Top + radius);
        Line(r.Right, r.Bottom - radius);
        Corner(r.Right - radius, r.Bottom);
        Line(r.Left + radius, r.Bottom);
        Corner(r.Left, r.Bottom - radius);
        Line(r.Left, r.Top + radius);
        Corner(r.Left + radius, r.Top);
        return new PathGeometry { Figures = { figure } };
    }

    static void Put(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    // Below the spotlight, else above, beside it, or, for a control taller than the window, in the window's lower right
    // corner over it. Placed from where the spotlight is going, so the card doesn't chase it as it glides.
    void PlaceCard(Rect hole)
    {
        double width = Math.Min(CardWidth, Math.Max(240, ActualWidth - Edge * 2));
        _card.Width = width;
        _card.Measure(new Size(width, double.PositiveInfinity));
        double height = _card.DesiredSize.Height;
        double maxX = Math.Max(Edge, ActualWidth - width - Edge), maxY = Math.Max(Edge, ActualHeight - height - Edge);

        (double X, double Y) at;
        if (hole.IsEmpty) at = ((ActualWidth - width) / 2, (ActualHeight - height) / 2);
        else if (hole.Bottom + Gap + height <= ActualHeight - Edge) at = (Math.Clamp(hole.X, Edge, maxX), hole.Bottom + Gap);
        else if (hole.Top - Gap - height >= Edge) at = (Math.Clamp(hole.X, Edge, maxX), hole.Top - Gap - height);
        else if (hole.Right + Gap + width <= ActualWidth - Edge) at = (hole.Right + Gap, Math.Clamp(hole.Y, Edge, maxY));
        else if (hole.Left - Gap - width >= Edge) at = (hole.Left - Gap - width, Math.Clamp(hole.Y, Edge, maxY));
        else at = (maxX, maxY);
        Canvas.SetLeft(_card, at.X);
        Canvas.SetTop(_card, at.Y);
    }

    static TextBlock Text(double size, Brush ink, bool semibold = false) => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        LineHeight = Math.Round(size * 1.55),
        TextWrapping = TextWrapping.Wrap,
        Foreground = ink,
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        CharacterSpacing = semibold && size <= 12 ? 80 : 0,
    };

    // The step's words, with the console's names set in the spotlight's color, as the guide sets them in its accent.
    static void Fill(TextBlock block, string line, IReadOnlyList<string> marked)
    {
        block.Inlines.Clear();
        foreach (var span in GuideContent.Spans(line, marked))
        {
            var run = new Run { Text = span.Text };
            if (span.Marked)
            {
                run.Foreground = Spot;
                run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            block.Inlines.Add(run);
        }
    }
}
