namespace Stx.Look;

public enum LedColor { Green, Red, Amber, Blue }

public enum LedState { Off, On, Strike, Busy }

/// <summary>Phosphor colors from the approved mockup: the bright core and the deep edge of the lens.</summary>
public static class LedColors
{
    public static string Core(LedColor color) => color switch
    {
        LedColor.Red => "#FF4242",
        LedColor.Amber => "#FFB020",
        LedColor.Blue => "#49C8FF",
        _ => "#3BFF7A",
    };

    public static string Deep(LedColor color) => color switch
    {
        LedColor.Red => "#7A0E0E",
        LedColor.Amber => "#7F4B00",
        LedColor.Blue => "#0B5578",
        _ => "#0E7A35",
    };
}
