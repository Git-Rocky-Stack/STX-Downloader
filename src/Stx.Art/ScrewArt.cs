namespace Stx.Art;

/// <summary>Stainless socket-cap screw seen from above: drilled recess, knurled rim, polished cap, hex socket.</summary>
public static class ScrewArt
{
    static readonly (double, double)[] CapStops =
    [
        (0, 251), (.17, 135), (.33, 228), (.52, 98), (.68, 244), (.85, 122), (1, 251),
    ];

    public static PixelBuffer Render(int size, int rotationDeg)
    {
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, radius = size / 2.0, rad = rotationDeg * Math.PI / 180;
        double cos = Math.Cos(rad), sin = Math.Sin(rad), apothem = radius * 0.36, pixel = 1.0 / radius;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center, dy = y - center, r = Math.Sqrt(dx * dx + dy * dy), u = r / radius;
                if (u > 1) continue;

                double turn = Math.Atan2(dy, dx) / (2 * Math.PI);
                // Light comes from the top left, so surfaces facing it read brighter.
                double facing = r > 0 ? -(dx + dy) / (r * 1.41421) : 0;

                if (u > 0.9)
                {
                    // Recess, with a thin catch of light on its lower edge.
                    double recess = facing < 0 ? 8 + 34 * -facing : 6;
                    buffer.Set(x, y, recess, recess, recess * 1.03, Math.Min(1, (1 - u) / pixel));
                    continue;
                }
                if (u > 0.74)
                {
                    double knurl = Math.Sin(turn * 2 * Math.PI * 34) > 0 ? 214 : 104;
                    knurl *= 0.72 + 0.28 * facing;
                    buffer.Set(x, y, knurl, knurl, knurl * 1.02);
                    continue;
                }

                double rx = dx * cos + dy * sin, ry = -dx * sin + dy * cos;
                double hex = ArtMath.HexDistance(rx, ry, apothem);
                if (hex < 0)
                {
                    // Socket interior: dark, with the far wall catching light.
                    double wall = Math.Clamp((rx + ry + apothem * 2.2) / (apothem * 4.4), 0, 1);
                    double v = 4 + 72 * Math.Pow(wall, 2.4);
                    buffer.Set(x, y, v, v, v * 1.02);
                    continue;
                }

                double cap = ArtMath.Conic(turn + 0.59, CapStops);
                if (hex < radius * 0.05) cap = Math.Min(255, cap * ((rx + ry) > 0 ? 1.25 : 0.55));
                if (u > 0.7) cap *= 0.82;
                buffer.Set(x, y, cap, cap, cap * 1.015);
            }
        }
        return buffer;
    }
}
