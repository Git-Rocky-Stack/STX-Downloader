using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace Stx.App;

/// <summary>
/// The caption strip every STX window wears: black in both modes, running straight into the chassis below it, with
/// silver buttons and no icon. The S is on the taskbar. Up here its dark plate would vanish.
/// </summary>
public static class TitleStrip
{
    public static readonly Windows.UI.Color Black = ColorHelper.FromArgb(255, 8, 9, 10);
    public static readonly Windows.UI.Color Silver = ColorHelper.FromArgb(255, 0xDF, 0xE3, 0xE8);
    public static readonly Windows.UI.Color SilverDim = ColorHelper.FromArgb(255, 0xA4, 0xAB, 0xB3);
    public static readonly Windows.UI.Color ButtonHover = ColorHelper.FromArgb(255, 0x2A, 0x2C, 0x2F);
    public static readonly Windows.UI.Color ButtonPressed = ColorHelper.FromArgb(255, 0x1D, 0x1F, 0x22);

    public static void Paint(AppWindow window)
    {
        var strip = window.TitleBar;
        strip.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        strip.BackgroundColor = strip.InactiveBackgroundColor = strip.ButtonBackgroundColor = strip.ButtonInactiveBackgroundColor = Black;
        strip.ForegroundColor = strip.ButtonForegroundColor = strip.ButtonHoverForegroundColor = strip.ButtonPressedForegroundColor = Silver;
        strip.InactiveForegroundColor = strip.ButtonInactiveForegroundColor = SilverDim;
        strip.ButtonHoverBackgroundColor = ButtonHover;
        strip.ButtonPressedBackgroundColor = ButtonPressed;
    }
}
