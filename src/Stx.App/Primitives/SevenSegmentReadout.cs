using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Stx.Art;

namespace Stx.App.Primitives;

/// <summary>An amber seven-segment readout behind glass. Text is right-aligned, as on real hardware.</summary>
public sealed partial class SevenSegmentReadout : Control
{
    const int ArtScale = 3;
    const double Gap = 2, PadX = 8, PadY = 6, Frame = 3;

    Canvas? _digitsHost;
    Image? _glass;
    Image[] _digits = [];
    int _count = 3;
    string _text = "";

    public SevenSegmentReadout()
    {
        DefaultStyleKey = typeof(SevenSegmentReadout);
        IsTabStop = false;
        IsHitTestVisible = false;
        ApplySize();
        SizeChanged += (_, _) => DrawGlass();
    }

    public string Text
    {
        get => _text;
        set { _text = value ?? ""; Paint(); }
    }

    /// <summary>How many digit positions the readout has.</summary>
    public int Digits
    {
        get => _count;
        set { _count = Math.Max(1, value); ApplySize(); Rebuild(); }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _digitsHost = GetTemplateChild("PART_Digits") as Canvas;
        _glass = GetTemplateChild("PART_Glass") as Image;
        Rebuild();
        DrawGlass();
    }

    void ApplySize()
    {
        Width = _count * ControlArt.DigitWidth + (_count - 1) * Gap + (PadX + Frame) * 2;
        Height = ControlArt.DigitHeight + (PadY + Frame) * 2;
    }

    void Rebuild()
    {
        if (_digitsHost is null) return;
        _digitsHost.Children.Clear();
        _digits = new Image[_count];
        for (int i = 0; i < _count; i++)
        {
            _digits[i] = new Image
            {
                Width = ControlArt.DigitWidth + ControlArt.DigitGlow * 2,
                Height = ControlArt.DigitHeight + ControlArt.DigitGlow * 2,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(_digits[i], i * (ControlArt.DigitWidth + Gap) - ControlArt.DigitGlow);
            Canvas.SetTop(_digits[i], -ControlArt.DigitGlow);
            _digitsHost.Children.Add(_digits[i]);
        }
        Paint();
    }

    void Paint()
    {
        AutomationProperties.SetName(this, _text.Trim());
        if (_digits.Length == 0) return;

        string shown = _text.Length > _count ? _text[^_count..] : _text.PadLeft(_count);
        int w = (int)(ControlArt.DigitWidth + ControlArt.DigitGlow * 2) * ArtScale;
        int h = (int)(ControlArt.DigitHeight + ControlArt.DigitGlow * 2) * ArtScale;
        for (int i = 0; i < _count; i++)
        {
            char c = shown[i];
            _digits[i].Source = ArtCache.Get($"digit:{c}", () => ControlArt.Digit(c, w, h));
        }
    }

    void DrawGlass()
    {
        if (_glass is null || ActualWidth < 1) return;
        double scale = XamlRoot?.RasterizationScale ?? 1;
        int w = (int)Math.Ceiling((ActualWidth - Frame * 2) * scale), h = (int)Math.Ceiling((ActualHeight - Frame * 2) * scale);
        if (w > 0 && h > 0) _glass.Source = ArtCache.Get($"glass:{w}x{h}", () => GlassArt.Render(w, h));
    }
}
