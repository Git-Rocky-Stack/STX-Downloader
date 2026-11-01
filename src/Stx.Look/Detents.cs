namespace Stx.Look;

/// <summary>Rotary selector positions, spread across a 270 degree sweep. Zero degrees points straight up.</summary>
public static class Detents
{
    const double Sweep = 270;

    public static double Angle(int index, int count)
    {
        if (count <= 1) return 0;
        return -Sweep / 2 + Sweep * Math.Clamp(index, 0, count - 1) / (count - 1);
    }

    public static int Next(int index, int count) => count <= 0 ? 0 : (index + 1) % count;

    public static int Previous(int index, int count) => count <= 0 ? 0 : (index - 1 + count) % count;

    /// <summary>Where an angle lands at a radius, relative to the knob's center.</summary>
    public static (double X, double Y) Point(double angle, double radius)
    {
        double rad = angle * Math.PI / 180;
        return (radius * Math.Sin(rad), -radius * Math.Cos(rad));
    }
}
