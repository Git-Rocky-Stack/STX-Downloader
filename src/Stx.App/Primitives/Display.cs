using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>A readout behind a glass lens: black bezel, recessed screen, content, then glass on top.</summary>
public sealed partial class Display : ContentControl
{
    /// <summary>Border 1 plus padding 5, on both sides. Matches the template in Generic.xaml.</summary>
    const double BezelTotal = 12;

    Image? _screen, _glass;
    DisplayTone _tone = DisplayTone.Amber;

    public Display()
    {
        DefaultStyleKey = typeof(Display);
        SizeChanged += (_, _) => Redraw();
    }

    public DisplayTone Tone
    {
        get => _tone;
        set { _tone = value; Redraw(); }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _screen = GetTemplateChild("PART_Screen") as Image;
        _glass = GetTemplateChild("PART_Glass") as Image;
        Redraw();
    }

    void Redraw()
    {
        // An Image with no source measures zero, so size the art from the control, less the bezel.
        double screenWidth = ActualWidth - BezelTotal, screenHeight = ActualHeight - BezelTotal;
        if (_screen is null || _glass is null || screenWidth < 1 || screenHeight < 1) return;

        double scale = XamlRoot?.RasterizationScale ?? 1;
        int w = (int)Math.Ceiling(screenWidth * scale), h = (int)Math.Ceiling(screenHeight * scale);
        _screen.Width = _glass.Width = screenWidth;
        _screen.Height = _glass.Height = screenHeight;
        var tone = _tone;
        _screen.Source = ArtCache.Get($"screen:{tone}:{w}x{h}", () => ScreenArt.Render(w, h, tone));
        _glass.Source = ArtCache.Get($"glass:{w}x{h}", () => GlassArt.Render(w, h));
    }
}
