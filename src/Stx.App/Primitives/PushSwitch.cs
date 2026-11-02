using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// A latching push switch: chrome bezel and a brushed cap that stays part way in once it is on, with an LED
/// slit that lights. It is a real ToggleButton, so keyboard and screen readers come with it.
/// Travel and the LED live in the template's visual states.
/// </summary>
public sealed partial class PushSwitch : ToggleButton
{
    const int ArtScale = 3;

    Image? _led;
    LedColor _litColor = LedColor.Green;

    public PushSwitch()
    {
        DefaultStyleKey = typeof(PushSwitch);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Loaded += (_, _) => { LookState.Changed += PaintLabel; PaintLabel(); };
        Unloaded += (_, _) => LookState.Changed -= PaintLabel;
        PaintLabel();
    }

    /// <summary>The color its LED shows when latched on.</summary>
    public LedColor LitColor
    {
        get => _litColor;
        set { _litColor = value; PaintLed(); }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        int bezelSize = (int)ControlArt.SwitchBezelLogical * ArtScale, capSize = (int)ControlArt.SwitchCapLogical * ArtScale;
        if (GetTemplateChild("PART_Bezel") is Image bezel) bezel.Source = ArtCache.Get("switch:bezel", () => ControlArt.SwitchBezel(bezelSize));
        if (GetTemplateChild("PART_Cap") is Image cap) cap.Source = ArtCache.Get("switch:cap", () => ControlArt.SwitchCap(capSize));
        if (GetTemplateChild("PART_Shade") is Image shade) shade.Source = ArtCache.Get("switch:shade", () => ControlArt.SwitchCapShade(capSize));
        _led = GetTemplateChild("PART_Led") as Image;
        PaintLed();
    }

    void PaintLed()
    {
        if (_led is null) return;
        int capSize = (int)ControlArt.SwitchCapLogical * ArtScale;
        var color = _litColor;
        _led.Source = ArtCache.Get($"switch:led:{color}", () => ControlArt.SwitchLed(capSize, color));
    }

    /// <summary>The label follows the mode, not the Windows theme.</summary>
    void PaintLabel() => Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);
}
