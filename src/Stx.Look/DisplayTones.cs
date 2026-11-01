namespace Stx.Look;

public enum DisplayTone { Amber, Cyan }

public static class DisplayTones
{
    /// <summary>Blur radii, stacked from a tight core to a wide halo. Thin glyphs have little coverage, so one layer reads too weak.</summary>
    public static readonly IReadOnlyList<float> GlowBlurs = [2.5f, 9f, 21.6f, 21.6f];

    /// <summary>Opacity per blur layer. The core stays below full strength, or letters read as outlined.</summary>
    public static readonly IReadOnlyList<float> GlowOpacities = [0.5f, 0.8f, 1f, 1f];

    public static string Text(DisplayTone tone, bool dimmed) => (tone, dimmed) switch
    {
        (DisplayTone.Amber, false) => "#FFC865",
        (DisplayTone.Amber, true) => "#B07F2C",
        (DisplayTone.Cyan, false) => "#8AE8FF",
        _ => "#3B9DBD",
    };

    public static string ScreenCenter(DisplayTone tone) => tone == DisplayTone.Amber ? "#3A2506" : "#0F3644";

    public static string ScreenEdge(DisplayTone tone) => tone == DisplayTone.Amber ? "#0A0601" : "#020C11";
}
