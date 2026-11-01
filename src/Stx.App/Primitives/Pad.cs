using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.Art;
using Stx.Look;
using Windows.UI;

namespace Stx.App.Primitives;

/// <summary>
/// A silicone pad in a milled well. It is a real Button, so keyboard and screen readers come with it.
/// Press travel and hover live in the template's visual states.
/// </summary>
public sealed partial class Pad : Button
{
    Image? _bloom, _well, _face, _shade;
    bool _lit;
    PadTone _litTone = PadTone.Green;

    public Pad()
    {
        DefaultStyleKey = typeof(Pad);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => { LookState.Changed += Redraw; Redraw(); };
        Unloaded += (_, _) => LookState.Changed -= Redraw;
    }

    public bool Lit
    {
        get => _lit;
        set { _lit = value; Redraw(); }
    }

    /// <summary>The color it lights in. Green for ready or running, blue for go.</summary>
    public PadTone LitTone
    {
        get => _litTone;
        set { _litTone = value; Redraw(); }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _bloom = GetTemplateChild("PART_Bloom") as Image;
        _well = GetTemplateChild("PART_Well") as Image;
        _face = GetTemplateChild("PART_Face") as Image;
        _shade = GetTemplateChild("PART_Shade") as Image;
        Redraw();
    }

    void Redraw()
    {
        var tone = _lit ? _litTone : PadTone.Off;
        string hex = PadTones.Legend(tone);
        Foreground = new SolidColorBrush(Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));

        if (_bloom is null || _well is null || _face is null || _shade is null || ActualWidth < 1 || ActualHeight < 1) return;

        double scale = XamlRoot?.RasterizationScale ?? 1;
        var mode = LookState.Mode;
        int wellW = Px(ActualWidth, scale), wellH = Px(ActualHeight, scale);
        double faceWidth = ActualWidth - ControlArt.WellInset * 2;
        double faceHeight = ActualHeight - ControlArt.WellInset * 2 - ControlArt.PadDepth;
        int faceW = Px(faceWidth, scale), faceH = Px(faceHeight, scale);
        int bloomW = Px(faceWidth + ControlArt.PadBloomOverhang * 2, scale), bloomH = Px(faceHeight + ControlArt.PadBloomOverhang * 2, scale);

        _well.Source = ArtCache.Get($"pad:well:{mode}:{wellW}x{wellH}", () => ControlArt.PadWell(wellW, wellH, scale, mode));
        _face.Source = ArtCache.Get($"pad:face:{tone}:{faceW}x{faceH}", () => ControlArt.PadFace(faceW, faceH, scale, tone));
        _shade.Source = ArtCache.Get($"pad:shade:{faceW}x{faceH}", () => ControlArt.PadPressShade(faceW, faceH, scale));
        if (tone == PadTone.Off)
        {
            _bloom.Source = null;
        }
        else
        {
            _bloom.Source = ArtCache.Get($"pad:bloom:{tone}:{bloomW}x{bloomH}", () => ControlArt.PadBloom(bloomW, bloomH, scale, tone));
        }
    }

    static int Px(double logical, double scale) => (int)Math.Ceiling(logical * scale);
}
