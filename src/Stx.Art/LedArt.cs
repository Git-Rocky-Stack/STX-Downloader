using Stx.Look;

namespace Stx.Art;

/// <summary>
/// LED and bargraph segment art. Lit parts are larger than the part itself, so their bloom lands on
/// whatever surrounds them. Sizes below are logical pixels; every method draws to the size it is given.
/// </summary>
public static class LedArt
{
    public const double LedLogical = 16;
    public const double LedOverhang = 24;
    public const double LedArtLogical = LedLogical + LedOverhang * 2;

    public const double SegmentWidth = 16;
    public const double SegmentHeight = 14;
    public const double SegmentOverhang = 8;
    public const double SegmentArtWidth = SegmentWidth + SegmentOverhang * 2;
    public const double SegmentArtHeight = SegmentHeight + SegmentOverhang * 2;

    const double BezelRadius = 8, ShadowRadius = 9, LensRadius = 5.5;

    static readonly (double, double)[] BezelStops =
    [
        (0, 229), (.2, 104), (.4, 200), (.6, 81), (.8, 219), (1, 229),
    ];

    /// <summary>Chrome bezel and the dark, unlit lens. Sits under the lit layers.</summary>
    public static PixelBuffer Housing(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, perPixel = LedArtLogical / size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = (x - center) * perPixel, dy = (y - center) * perPixel, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > ShadowRadius) continue;

                if (d > BezelRadius)
                {
                    buffer.Set(x, y, 0, 0, 0, Math.Min(1, (ShadowRadius - d) / perPixel));
                }
                else if (d > LensRadius)
                {
                    double turn = Math.Atan2(dx, -dy) / (2 * Math.PI) - 210.0 / 360;
                    double v = ArtMath.Conic(turn, BezelStops);
                    buffer.Set(x, y, v, v * 1.005, v * 1.02);
                }
                else
                {
                    double t = Math.Sqrt(Sq(dx + 0.9) + Sq(dy + 1.3)) / (LensRadius * 1.3);
                    double v = t < 0.62 ? Mix(76, 30, t / 0.62) : Mix(30, 12, Math.Min(1, (t - 0.62) / 0.38));
                    v += Specular(dx, dy) * 0.5 * (255 - v);
                    buffer.Set(x, y, v, v * 1.03, v);
                }
            }
        }
        return buffer;
    }

    /// <summary>The lens at full brightness and the glow it throws onto the plate.</summary>
    public static PixelBuffer Lit(LedColor color, int size)
    {
        var core = Parse(LedColors.Core(color));
        var deep = Parse(LedColors.Deep(color));
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, perPixel = LedArtLogical / size, reach = LedArtLogical / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = (x - center) * perPixel, dy = (y - center) * perPixel, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= reach) continue;

                if (d <= LensRadius)
                {
                    double t = Math.Sqrt(dx * dx + Sq(dy - 0.44)) / (LensRadius * 1.04);
                    var c = t < 0.34 ? Blend((255, 255, 255), core, t / 0.34) : Blend(core, deep, Math.Min(1, (t - 0.34) / 0.62));
                    c = Blend(c, (255, 255, 255), Specular(dx, dy));
                    buffer.Set(x, y, c.R, c.G, c.B);
                }
                else
                {
                    double out_ = d - LensRadius;
                    double a = 0.9 * Gauss(out_, 3.5) + 0.42 * Gauss(out_, 11) + 0.14 * Gauss(out_, 18);
                    buffer.Set(x, y, core.R, core.G, core.B, Math.Min(1, a) * Fade(d / reach));
                }
            }
        }
        return buffer;
    }

    /// <summary>A hotter layer that fades in over <see cref="Lit"/> for the flashes of a strike.</summary>
    public static PixelBuffer Flare(LedColor color, int size)
    {
        var core = Parse(LedColors.Core(color));
        var hot = Blend(core, (255, 255, 255), 0.55);
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, perPixel = LedArtLogical / size, reach = LedArtLogical / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = (x - center) * perPixel, dy = (y - center) * perPixel, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= reach) continue;

                if (d <= LensRadius)
                {
                    var c = Blend((255, 255, 255), hot, d / LensRadius);
                    buffer.Set(x, y, c.R, c.G, c.B);
                }
                else
                {
                    double out_ = d - LensRadius;
                    double a = Gauss(out_, 5) + 0.5 * Gauss(out_, 14);
                    buffer.Set(x, y, hot.R, hot.G, hot.B, Math.Min(1, a) * Fade(d / reach));
                }
            }
        }
        return buffer;
    }

    public static PixelBuffer SegmentLit(LedColor color, int width, int height) => Segment(Parse(LedColors.Core(color)), width, height, 1);

    /// <summary>A lit segment in any phosphor color (#RRGGBB): one step of a meter's color ramp.</summary>
    public static PixelBuffer SegmentLit(string coreHex, int width, int height) => Segment(Parse(coreHex), width, height, 1);

    /// <summary>The peak-hold segment burns about half again as bright.</summary>
    public static PixelBuffer SegmentPeak(LedColor color, int width, int height) => Segment(Parse(LedColors.Core(color)), width, height, 1.5);

    /// <summary>The peak-hold segment in any phosphor color (#RRGGBB).</summary>
    public static PixelBuffer SegmentPeak(string coreHex, int width, int height) => Segment(Parse(coreHex), width, height, 1.5);

    /// <summary>
    /// A dead segment keeping a trace of any phosphor color (#RRGGBB): the plate's near-black tinted by the color,
    /// darkest just below the middle, as the named colors' are.
    /// </summary>
    public static PixelBuffer SegmentUnlit(string coreHex, int width, int height)
    {
        var core = Parse(coreHex);
        return Unlit((Blend((22, 24, 24), core, 0.09), Blend((10, 11, 11), core, 0.04), Blend((16, 18, 18), core, 0.06)), width, height);
    }

    /// <summary>A dead segment: dark, keeping a trace of its color. Has no overhang.</summary>
    public static PixelBuffer SegmentUnlit(LedColor color, int width, int height)
    {
        ((double R, double G, double B) Top, (double R, double G, double B) Mid, (double R, double G, double B) Bottom) tones = color switch
        {
            LedColor.Red => ((44, 18, 18), (20, 6, 6), (29, 10, 10)),
            LedColor.Amber => ((42, 32, 13), (19, 14, 4), (29, 22, 8)),
            _ => ((26, 35, 29), (12, 17, 14), (20, 26, 22)),
        };
        return Unlit(tones, width, height);
    }

    static PixelBuffer Unlit(((double R, double G, double B) Top, (double R, double G, double B) Mid, (double R, double G, double B) Bottom) tones, int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            double t = height > 1 ? (double)y / (height - 1) : 0;
            var c = t < 0.7 ? Blend(tones.Top, tones.Mid, t / 0.7) : Blend(tones.Mid, tones.Bottom, (t - 0.7) / 0.3);
            for (int x = 0; x < width; x++) buffer.Set(x, y, c.R, c.G, c.B);
        }
        return buffer;
    }

    static PixelBuffer Segment((double R, double G, double B) core, int width, int height, double gain)
    {
        var hot = Blend(core, (255, 255, 255), 0.88);
        var edge = (R: core.R * 0.66, G: core.G * 0.66, B: core.B * 0.66);
        var buffer = new PixelBuffer(width, height);
        double midX = SegmentArtWidth / 2, midY = SegmentArtHeight / 2;
        double halfW = SegmentWidth / 2, halfH = SegmentHeight / 2;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double lx = (x + 0.5) * SegmentArtWidth / width, ly = (y + 0.5) * SegmentArtHeight / height;
                double ox = Math.Max(Math.Abs(lx - midX) - halfW, 0), oy = Math.Max(Math.Abs(ly - midY) - halfH, 0);
                double d = Math.Sqrt(ox * ox + oy * oy);

                if (d <= 0)
                {
                    double t = Math.Sqrt(Sq((lx - midX) / (SegmentWidth * 0.7)) + Sq((ly - midY) / (SegmentHeight * 0.7)));
                    var c = t < 0.42 ? Blend(hot, core, t / 0.42) : Blend(core, edge, Math.Min(1, (t - 0.42) / 0.58));
                    buffer.Set(x, y, c.R * gain, c.G * gain, c.B * gain);
                }
                else if (d < SegmentOverhang)
                {
                    double a = 0.42 * Gauss(d, 2) + 0.22 * Gauss(d, 5);
                    buffer.Set(x, y, core.R, core.G, core.B, Math.Min(1, a * gain) * Fade(d / SegmentOverhang));
                }
            }
        }
        return buffer;
    }

    /// <summary>The small reflection on the dome of the lens, strongest at its middle.</summary>
    static double Specular(double dx, double dy) =>
        0.95 * Math.Clamp(1.3 - Math.Sqrt(Sq((dx + 1) / 2) + Sq((dy + 2.5) / 1.5)) * 1.3, 0, 1);

    static double Gauss(double x, double width) => Math.Exp(-(x / width) * (x / width));

    /// <summary>Takes a glow to nothing at the art's edge, so no square outline shows on the plate.</summary>
    static double Fade(double u) => Sq(Math.Max(0, 1 - u * u));

    static double Sq(double v) => v * v;

    static double Mix(double a, double b, double t) => a + (b - a) * t;

    static (double R, double G, double B) Blend((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));

    static (double R, double G, double B) Parse(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
