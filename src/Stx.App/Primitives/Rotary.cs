using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Stx.Art;
using Stx.Look;
using Windows.System;
using Windows.UI;

namespace Stx.App.Primitives;

/// <summary>
/// A rotary selector with a printed scale. The knurl and pointer turn, while the cap's highlights and the
/// shading stay put, because the light does not move. Arrow keys and clicks step it from detent to detent.
/// </summary>
public sealed partial class Rotary : Control
{
    const double Dial = 176, Knob = ControlArt.KnobLogical, LabelRadius = 74, TickOuter = 61, TickLength = 7;
    const int ArtSize = 300;
    const float TurnSeconds = 0.22f;

    Canvas? _host;
    Microsoft.UI.Composition.Visual? _knurlVisual, _pointerVisual;
    readonly List<TextBlock> _labels = [];
    readonly List<Rectangle> _ticks = [];
    IReadOnlyList<string> _items = [];
    int _selected;

    public Rotary()
    {
        DefaultStyleKey = typeof(Rotary);
        Width = Height = Dial;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Loaded += (_, _) => { LookState.Changed += PaintScale; PaintScale(); };
        Unloaded += (_, _) => LookState.Changed -= PaintScale;
    }

    public event EventHandler<int>? SelectionChanged;

    /// <summary>What this knob selects, spoken before the current position.</summary>
    public string Label { get; set; } = "";

    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            _selected = _items.Count == 0 ? 0 : Math.Clamp(_selected, 0, _items.Count - 1);
            BuildScale();
            Turn(animate: false);
            UpdateName();
        }
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int next = _items.Count == 0 ? 0 : Math.Clamp(value, 0, _items.Count - 1);
            if (next == _selected) return;
            _selected = next;
            Turn(animate: true);
            PaintScale();
            UpdateName();
            SelectionChanged?.Invoke(this, _selected);
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Host") as Canvas;
        if (_host is null) return;

        double left = (Dial - Knob) / 2;
        double shadowSize = Knob + ControlArt.KnobShadowOverhang * 2;
        _host.Children.Clear();
        BuildScale();
        _host.Children.Add(Part(ArtCache.Get("knob:shadow", () => ControlArt.KnobShadow(260)), left - ControlArt.KnobShadowOverhang, left - ControlArt.KnobShadowOverhang, shadowSize));

        var knurl = Part(ArtCache.Get("knob:knurl", () => ControlArt.Knurl(ArtSize)), left, left, Knob);
        var pointer = Part(ArtCache.Get("knob:pointer", () => ControlArt.Indicator(ArtSize)), left, left, Knob);
        _host.Children.Add(knurl);
        _host.Children.Add(Part(ArtCache.Get("knob:shade", () => ControlArt.KnobShade(ArtSize)), left, left, Knob));
        _host.Children.Add(Part(ArtCache.Get("knob:cap", () => ControlArt.KnobCap(ArtSize)), left, left, Knob));
        _host.Children.Add(pointer);

        var center = new Vector3((float)(Knob / 2), (float)(Knob / 2), 0);
        _knurlVisual = ElementCompositionPreview.GetElementVisual(knurl);
        _pointerVisual = ElementCompositionPreview.GetElementVisual(pointer);
        _knurlVisual.CenterPoint = center;
        _pointerVisual.CenterPoint = center;
        Turn(animate: false);
        PaintScale();
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Right:
            case VirtualKey.Up:
                SelectedIndex = Detents.Next(_selected, _items.Count);
                e.Handled = true;
                break;
            case VirtualKey.Left:
            case VirtualKey.Down:
                SelectedIndex = Detents.Previous(_selected, _items.Count);
                e.Handled = true;
                break;
            default:
                base.OnKeyDown(e);
                break;
        }
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        SelectedIndex = Detents.Next(_selected, _items.Count);
        e.Handled = true;
    }

    static Image Part(ImageSource source, double left, double top, double size)
    {
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        return image;
    }

    void BuildScale()
    {
        if (_host is null) return;
        foreach (var label in _labels) _host.Children.Remove(label);
        foreach (var tick in _ticks) _host.Children.Remove(tick);
        _labels.Clear();
        _ticks.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            double angle = Detents.Angle(i, _items.Count);
            var tick = new Rectangle { Width = 2, Height = TickLength, IsHitTestVisible = false };
            var turn = new TransformGroup();
            turn.Children.Add(new TranslateTransform { X = -1, Y = -TickOuter });
            turn.Children.Add(new RotateTransform { Angle = angle });
            tick.RenderTransform = turn;
            Canvas.SetLeft(tick, Dial / 2);
            Canvas.SetTop(tick, Dial / 2);
            _ticks.Add(tick);
            _host.Children.Insert(0, tick);

            var text = new TextBlock
            {
                Text = _items[i],
                FontFamily = new FontFamily("Segoe UI"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 9.5,
                CharacterSpacing = 60,
                IsHitTestVisible = false,
            };
            text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var (x, y) = Detents.Point(angle, LabelRadius);
            Canvas.SetLeft(text, Dial / 2 + x - text.DesiredSize.Width / 2);
            Canvas.SetTop(text, Dial / 2 + y - text.DesiredSize.Height / 2);
            _labels.Add(text);
            _host.Children.Insert(0, text);
        }
        PaintScale();
    }

    void PaintScale()
    {
        var tokens = LookTokens.For(LookState.Mode);
        var ink = Brush(tokens["Ink"]);
        var dim = Brush(tokens["InkDim"]);
        foreach (var tick in _ticks) tick.Fill = dim;
        for (int i = 0; i < _labels.Count; i++) _labels[i].Foreground = i == _selected ? ink : dim;
    }

    static SolidColorBrush Brush(string hex) =>
        new(Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));

    void UpdateName()
    {
        string item = _selected < _items.Count ? _items[_selected] : "";
        AutomationProperties.SetName(this, string.IsNullOrEmpty(Label) ? item : $"{Label}: {item}");
    }

    void Turn(bool animate)
    {
        if (_knurlVisual is null || _pointerVisual is null) return;
        float angle = (float)Detents.Angle(_selected, _items.Count);
        if (!animate)
        {
            _knurlVisual.RotationAngleInDegrees = angle;
            _pointerVisual.RotationAngleInDegrees = angle;
            return;
        }

        var compositor = _knurlVisual.Compositor;
        var (x1, y1, x2, y2) = PressTravel.TurnEasing(LookState.ReduceMotion);
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
        var turn = compositor.CreateScalarKeyFrameAnimation();
        turn.InsertKeyFrame(1, angle, easing);
        turn.Duration = TimeSpan.FromSeconds(TurnSeconds);
        _knurlVisual.StartAnimation("RotationAngleInDegrees", turn);
        _pointerVisual.StartAnimation("RotationAngleInDegrees", turn);
    }
}
