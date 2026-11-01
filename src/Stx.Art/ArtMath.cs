namespace Stx.Art;

internal static class ArtMath
{
    /// <summary>Interpolates around a circle. Position wraps, so stops can describe metal reflections.</summary>
    public static double Conic(double turn, (double At, double Value)[] stops)
    {
        turn = (turn % 1 + 1) % 1;
        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (turn <= stops[i + 1].At)
            {
                double span = stops[i + 1].At - stops[i].At;
                return stops[i].Value + (stops[i + 1].Value - stops[i].Value) * (turn - stops[i].At) / span;
            }
        }
        return stops[^1].Value;
    }

    /// <summary>Signed distance from a regular hexagon with flat top and bottom. Negative is inside.</summary>
    public static double HexDistance(double x, double y, double apothem)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y);
        return Math.Max(ax * 0.866025 + ay * 0.5, ay) - apothem;
    }
}
