namespace Stx.Look;

/// <summary>A soft highlight that follows the pointer. It's wide and short, as light spreads along horizontally brushed metal.</summary>
public static class PointerLightLayout
{
    public const float Width = 760, Height = 260;

    public static (float X, float Y) OffsetFor(float pointerX, float pointerY) => (pointerX - Width / 2, pointerY - Height / 2);

    /// <summary>Out of 255. Bright metal needs more to show at all.</summary>
    public static byte PeakAlpha(LookMode mode) => mode == LookMode.Light ? (byte)64 : (byte)22;
}
