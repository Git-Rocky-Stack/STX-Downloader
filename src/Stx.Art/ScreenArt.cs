using Stx.Look;

namespace Stx.Art;

/// <summary>The recessed panel behind the glass: backlight falloff, shadow from the bezel, and a fine pixel grid.</summary>
public static class ScreenArt
{
    public static PixelBuffer Render(int width, int height, DisplayTone tone)
    {
        var (cr, cg, cb) = Rgb(DisplayTones.ScreenCenter(tone));
        var (er, eg, eb) = Rgb(DisplayTones.ScreenEdge(tone));
        var buffer = new PixelBuffer(width, height);
        double cx = width / 2.0, cy = height * 0.45;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double u = Math.Min(1, Math.Sqrt(Sq((x - cx) / (width * 0.62)) + Sq((y - cy) / (height * 0.85))));
                double r = cr + (er - cr) * u, g = cg + (eg - cg) * u, b = cb + (eb - cb) * u;

                // Bezel shadow falling across the recessed screen, heaviest from the top.
                double fromEdge = Math.Min(Math.Min(x, width - 1 - x) / 8.0, Math.Min(y / 12.0, (height - 1 - y) / 6.0));
                double shade = 0.2 + 0.8 * Math.Clamp(fromEdge, 0, 1);

                double grid = x % 2 == 0 || y % 2 == 0 ? 0.74 : 1;
                buffer.Set(x, y, r * shade * grid, g * shade * grid, b * shade * grid);
            }
        }
        return buffer;
    }

    static double Sq(double v) => v * v;

    static (double, double, double) Rgb(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
