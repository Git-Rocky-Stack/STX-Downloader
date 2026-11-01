using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// An LED bargraph in a recessed well behind glass. Each color has one lit bitmap, shared by every segment.
/// A new reading touches only the segments that changed.
/// </summary>
public sealed partial class Bargraph : Control
{
    const int ArtScale = 3;
    /// <summary>Border 1 plus padding 5, on both sides. Matches the template in Generic.xaml.</summary>
    const double WellInset = 12;
    const double Gap = 2;

    Canvas? _segmentsHost;
    Image? _glass;
    Image[] _lit = [], _peak = [];
    Image[] _unlit = [];
    int _segments = 30, _litCount;
    int _peakIndex = -1;
    double _value, _peakValue = double.NaN;
    BargraphZones _zones = BargraphZones.Progress;

    public Bargraph()
    {
        DefaultStyleKey = typeof(Bargraph);
        IsTabStop = false;
        IsHitTestVisible = false;
        SizeChanged += (_, _) => Layout();
    }

    /// <summary>Percent, 0 to 100.</summary>
    public double Value
    {
        get => _value;
        set { _value = value; Paint(); }
    }

    /// <summary>Peak-hold percent. NaN shows no peak.</summary>
    public double Peak
    {
        get => _peakValue;
        set { _peakValue = value; Paint(); }
    }

    public BargraphZones Zones
    {
        get => _zones;
        set { _zones = value; Rebuild(); }
    }

    public int Segments
    {
        get => _segments;
        set { _segments = Math.Max(1, value); Rebuild(); }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _segmentsHost = GetTemplateChild("PART_Segments") as Canvas;
        _glass = GetTemplateChild("PART_Glass") as Image;
        Rebuild();
    }

    void Rebuild()
    {
        if (_segmentsHost is null) return;

        _segmentsHost.Children.Clear();
        _unlit = new Image[_segments];
        _lit = new Image[_segments];
        _peak = new Image[_segments];
        _litCount = 0;
        _peakIndex = -1;

        int artW = (int)LedArt.SegmentArtWidth * ArtScale, artH = (int)LedArt.SegmentArtHeight * ArtScale;
        int coreW = (int)LedArt.SegmentWidth * ArtScale, coreH = (int)LedArt.SegmentHeight * ArtScale;

        for (int i = 0; i < _segments; i++)
        {
            var color = BargraphLogic.ZoneColor(i, _segments, _zones);
            _unlit[i] = Layer(ArtCache.Get($"seg:unlit:{color}", () => LedArt.SegmentUnlit(color, coreW, coreH)), visible: true);
            _segmentsHost.Children.Add(_unlit[i]);
        }
        // Lit sprites go above every dead segment, so their bloom can land on the neighbors.
        for (int i = 0; i < _segments; i++)
        {
            var color = BargraphLogic.ZoneColor(i, _segments, _zones);
            _lit[i] = Layer(ArtCache.Get($"seg:lit:{color}", () => LedArt.SegmentLit(color, artW, artH)), visible: false);
            _peak[i] = Layer(ArtCache.Get($"seg:peak:{color}", () => LedArt.SegmentPeak(color, artW, artH)), visible: false);
            _segmentsHost.Children.Add(_lit[i]);
            _segmentsHost.Children.Add(_peak[i]);
        }
        Layout();
        Paint();
    }

    static Image Layer(Microsoft.UI.Xaml.Media.ImageSource source, bool visible) => new()
    {
        Source = source,
        Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
        IsHitTestVisible = false,
        Opacity = visible ? 1 : 0,
    };

    void Layout()
    {
        if (_segmentsHost is null || _glass is null || _unlit.Length == 0) return;
        double width = ActualWidth - WellInset, height = ActualHeight - WellInset;
        if (width < 1 || height < 1) return;

        double segment = (width - Gap * (_segments - 1)) / _segments;
        if (segment <= 0) return;
        double overhangX = segment * LedArt.SegmentOverhang / LedArt.SegmentWidth;
        double overhangY = height * LedArt.SegmentOverhang / LedArt.SegmentHeight;

        for (int i = 0; i < _segments; i++)
        {
            double x = i * (segment + Gap);
            Place(_unlit[i], x, 0, segment, height);
            Place(_lit[i], x - overhangX, -overhangY, segment + overhangX * 2, height + overhangY * 2);
            Place(_peak[i], x - overhangX, -overhangY, segment + overhangX * 2, height + overhangY * 2);
        }

        double scale = XamlRoot?.RasterizationScale ?? 1;
        int w = (int)Math.Ceiling((ActualWidth - 2) * scale), h = (int)Math.Ceiling((ActualHeight - 2) * scale);
        if (w > 0 && h > 0) _glass.Source = ArtCache.Get($"glass:{w}x{h}", () => GlassArt.Render(w, h));
    }

    static void Place(Image image, double left, double top, double width, double height)
    {
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        image.Width = width;
        image.Height = height;
    }

    void Paint()
    {
        if (_lit.Length == 0) return;

        int newLit = BargraphLogic.LitCount(_value, _segments);
        int newPeak = BargraphLogic.PeakIndex(_peakValue, _segments);
        foreach (int i in BargraphLogic.Changed(_litCount, newLit, _peakIndex, newPeak))
        {
            _lit[i].Opacity = i < newLit || i == newPeak ? 1 : 0;
            _peak[i].Opacity = i == newPeak ? 1 : 0;
        }
        _litCount = newLit;
        _peakIndex = newPeak;
    }
}
