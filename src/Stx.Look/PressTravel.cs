namespace Stx.Look;

public enum PadTone { Off, Green, Blue }

/// <summary>How far and how quickly parts move when you touch them.</summary>
public static class PressTravel
{
    public const double Pixels = 3;

    /// <summary>A cubic bezier for levers and knobs. It overshoots and settles, unless motion is reduced.</summary>
    public static (float X1, float Y1, float X2, float Y2) TurnEasing(bool reduceMotion) =>
        reduceMotion ? (0.2f, 0f, 0.4f, 1f) : (0.3f, 1.5f, 0.5f, 1f);
}

/// <summary>Pad faces are backlit through rubber. The center stays dark enough for the legend to pass AA.</summary>
public static class PadTones
{
    public static string Center(PadTone tone) => tone switch
    {
        PadTone.Green => "#1D8147",
        PadTone.Blue => "#1A7BA3",
        _ => "#34383C",
    };

    public static string Middle(PadTone tone) => tone switch
    {
        PadTone.Green => "#14663A",
        PadTone.Blue => "#126285",
        _ => "#2D3034",
    };

    public static string Rim(PadTone tone) => tone switch
    {
        PadTone.Green => "#0B3A20",
        PadTone.Blue => "#0A3A4F",
        _ => "#1B1D20",
    };

    public static string Legend(PadTone tone) => tone == PadTone.Off ? "#EEF1F4" : "#FFFFFF";

    /// <summary>The color a lit pad throws onto the plate. An unlit pad throws none.</summary>
    public static string Bloom(PadTone tone) => tone == PadTone.Blue ? "#49C8FF" : "#3BFF7A";
}
