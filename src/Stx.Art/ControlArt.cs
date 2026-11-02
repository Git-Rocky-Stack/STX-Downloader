using Stx.Look;

namespace Stx.Art;

/// <summary>Art for readouts, pads, toggles and rotary knobs. Parts that turn carry no directional light of their own.</summary>
public static class ControlArt
{
    public const double DigitWidth = 13;
    public const double DigitHeight = 23;
    public const double DigitGlow = 6;

    public const double KnobLogical = 100;
    public const double KnobShadowOverhang = 15;

    public const double PadRadius = 7;
    public const double PadBloomOverhang = 28;
    public const double WellInset = 3;
    /// <summary>How far the pad's body sits above the well floor. Press travel uses up most of it.</summary>
    public const double PadDepth = 4;

    public const double SwitchBezelLogical = 48;
    public const double SwitchOpening = 42;
    public const double SwitchCapLogical = 38;

    static readonly (Segments Segment, (double X, double Y)[] Points)[] DigitShapes =
    [
        (Segments.A, [(2, 0), (11, 0), (9, 2), (4, 2)]),
        (Segments.B, [(11, 1), (13, 3), (13, 9), (11, 11), (10, 9), (10, 3)]),
        (Segments.C, [(11, 12), (13, 14), (13, 20), (11, 22), (10, 20), (10, 14)]),
        (Segments.D, [(2, 23), (4, 21), (9, 21), (11, 23)]),
        (Segments.E, [(2, 12), (3, 14), (3, 20), (2, 22), (0, 20), (0, 14)]),
        (Segments.F, [(2, 1), (3, 3), (3, 9), (2, 11), (0, 9), (0, 3)]),
        (Segments.G, [(2, 11.5), (4, 10.5), (9, 10.5), (11, 11.5), (9, 12.5), (4, 12.5)]),
    ];

    static readonly (double, double)[] CapStops =
    [
        (0, 244), (.13, 129), (.25, 225), (.38, 106), (.5, 244), (.63, 129), (.75, 225), (.88, 106), (1, 244),
    ];

    /// <summary>One seven-segment digit with its dead bars, lit bars and glow. Width and height include the glow margin.</summary>
    public static PixelBuffer Digit(char c, int width, int height)
    {
        const double Lit = 255, LitG = 157, LitB = 46, DeadR = 42, DeadG = 23, DeadB = 8;
        var lit = SevenSegment.For(c);
        var on = new double[width * height];
        var off = new double[width * height];
        double perX = (DigitWidth + DigitGlow * 2) / width, perY = (DigitHeight + DigitGlow * 2) / height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                foreach (var (segment, points) in DigitShapes)
                {
                    double cover = Coverage(x, y, perX, perY, points);
                    if (cover <= 0) continue;
                    if (lit.HasFlag(segment)) on[y * width + x] = Math.Max(on[y * width + x], cover);
                    else off[y * width + x] = Math.Max(off[y * width + x], cover);
                }
            }
        }

        int artPerLogical = Math.Max(1, (int)Math.Round(1 / perX));
        var nearGlow = BoxBlur(on, width, height, artPerLogical);
        var wideGlow = BoxBlur(on, width, height, artPerLogical * 2);

        var buffer = new PixelBuffer(width, height);
        for (int i = 0; i < on.Length; i++)
        {
            double glow = Math.Min(1, 0.9 * nearGlow[i] + 0.6 * wideGlow[i]);
            double alpha = glow, r = Lit * glow, g = LitG * glow, b = LitB * glow;
            (alpha, r, g, b) = Over(alpha, r, g, b, off[i], DeadR, DeadG, DeadB);
            (alpha, r, g, b) = Over(alpha, r, g, b, on[i], Lit, LitG, LitB);
            if (alpha <= 0) continue;
            buffer.Set(i % width, i / width, r / alpha, g / alpha, b / alpha, alpha);
        }
        return buffer;
    }

    /// <summary>The ridged skirt of a knob. It turns, so it has no light or shade baked in.</summary>
    public static PixelBuffer Knurl(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, radius = size / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center, dy = y - center, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= radius) continue;
                double ridge = Math.Clamp(Math.Sin(Math.Atan2(dy, dx) * 60) * 2 + 0.5, 0, 1);
                double v = Mix(10, 59, ridge);
                buffer.Set(x, y, v, v * 1.02, v * 1.05, Math.Min(1, radius - d));
            }
        }
        return buffer;
    }

    /// <summary>Light and shade across the knurl. It stays still while the knurl turns beneath it.</summary>
    public static PixelBuffer KnobShade(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, radius = size / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - center, dy = y - center, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= radius) continue;
                double t = (dx * 0.5736 + dy * 0.8192) / (radius * 2) + 0.5;
                double edge = Math.Min(1, radius - d);
                if (t < 0.42) buffer.Set(x, y, 255, 255, 255, 0.28 * (1 - t / 0.42) * edge);
                else buffer.Set(x, y, 0, 0, 0, 0.55 * (t - 0.42) / 0.58 * edge);
            }
        }
        return buffer;
    }

    /// <summary>The soft shadow a knob drops on the plate. Size includes the overhang on each side.</summary>
    public static PixelBuffer KnobShadow(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double total = KnobLogical + KnobShadowOverhang * 2, per = total / size, center = (size - 1) / 2.0;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = (x - center) * per, dy = (y - center) * per - 7;
                double d = Math.Sqrt(dx * dx + dy * dy) - KnobLogical / 2;
                double a = 0.8 * Math.Clamp(0.5 - d / 10, 0, 1);
                if (a > 0) buffer.Set(x, y, 0, 0, 0, a);
            }
        }
        return buffer;
    }

    /// <summary>The spun metal cap. Its bands belong to the light, not the knob, so it never rotates.</summary>
    public static PixelBuffer KnobCap(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double center = (size - 1) / 2.0, per = KnobLogical / size, radius = (KnobLogical / 2 - 14);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = (x - center) * per, dy = (y - center) * per, d = Math.Sqrt(dx * dx + dy * dy);
                if (d >= radius) continue;
                double turn = Math.Atan2(dx, -dy) / (2 * Math.PI) - 40.0 / 360;
                double v = ArtMath.Conic(turn, CapStops);
                v += Math.Sin(d * Math.PI * 2) * 5;
                if (d > radius - 1) v += 40;
                buffer.Set(x, y, v, v * 1.005, v * 1.012, Math.Min(1, (radius - d) / per));
            }
        }
        return buffer;
    }

    /// <summary>The glowing pointer, drawn pointing straight up in a frame the size of the knob.</summary>
    public static PixelBuffer Indicator(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double per = KnobLogical / size;
        const double Left = 48, Right = 52, Top = 4, Bottom = 31;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5) * per, ly = (y + 0.5) * per;
                double ox = Math.Max(Math.Max(Left - lx, lx - Right), 0), oy = Math.Max(Math.Max(Top - ly, ly - Bottom), 0);
                double d = Math.Sqrt(ox * ox + oy * oy);
                if (d <= 0)
                {
                    double t = (ly - Top) / (Bottom - Top);
                    buffer.Set(x, y, Mix(255, 168, t), Mix(255, 230, t), 255);
                }
                else if (d < 12)
                {
                    double a = (0.6 * Gauss(d, 3) + 0.35 * Gauss(d, 7)) * Sq(1 - d / 12);
                    buffer.Set(x, y, 73, 200, 255, a);
                }
            }
        }
        return buffer;
    }

    /// <summary>The milled well a pad sits in, with a catch of light on its lower lip.</summary>
    public static PixelBuffer PadWell(int width, int height, double scale, LookMode mode)
    {
        double highlight = LookTokens.For(mode)["EdgeHighlight"] is var edge && edge.Length == 9
            ? Convert.ToInt32(edge.Substring(1, 2), 16) / 255.0
            : 0.2;
        var buffer = new PixelBuffer(width, height);
        double lw = width / scale, lh = height / scale, perPixel = 1 / scale;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double lx = (x + 0.5) * perPixel, ly = (y + 0.5) * perPixel;
                double outer = RoundRect(lx, ly, lw, lh - 1, PadRadius + 2);
                if (outer > 0)
                {
                    // Lower lip: one logical pixel below the well.
                    double lip = RoundRect(lx, ly, lw, lh, PadRadius + 2);
                    if (lip < 0) buffer.Set(x, y, 255, 255, 255, highlight * Math.Min(1, -lip / perPixel));
                    continue;
                }
                double t = ly / lh;
                double v = Mix(5, 23, t);
                buffer.Set(x, y, v, v * 1.05, v * 1.15, Math.Min(1, -outer / perPixel));
            }
        }
        return buffer;
    }

    /// <summary>The rubber face of a pad. Light falls from the top, and the lower edge rolls away.</summary>
    public static PixelBuffer PadFace(int width, int height, double scale, PadTone tone)
    {
        var center = Parse(PadTones.Center(tone));
        var middle = Parse(PadTones.Middle(tone));
        var rim = Parse(PadTones.Rim(tone));
        var buffer = new PixelBuffer(width, height);
        double lw = width / scale, lh = height / scale, perPixel = 1 / scale;
        bool off = tone == PadTone.Off;
        double hotY = off ? 0.32 : 0.5, rx = off ? 0.8 : 0.7, ry = off ? 0.75 : 0.7;
        double topLight = off ? 0.26 : 0.4;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double lx = (x + 0.5) * perPixel, ly = (y + 0.5) * perPixel;
                double sd = RoundRect(lx, ly, lw, lh, PadRadius);
                if (sd >= 0) continue;

                double t = Math.Sqrt(Sq((lx - lw / 2) / (lw * rx)) + Sq((ly - lh * hotY) / (lh * ry)));
                var c = t < 0.58 ? Blend(center, middle, t / 0.58) : Blend(middle, rim, Math.Min(1, (t - 0.58) / 0.42));

                // Gloss across the upper third, stopping short of the sides and of the legend.
                double glossAcross = Smooth(0.04, 0.3, lx / lw) * Smooth(0.04, 0.3, 1 - lx / lw);
                double glossDown = Smooth(1, 4, ly) * (1 - Smooth(4, lh * 0.42, ly));
                c = Blend(c, (255, 255, 255), 0.14 * glossAcross * glossDown);
                if (ly < 1.5) c = Blend(c, (255, 255, 255), topLight);
                double fromBottom = lh - ly;
                if (fromBottom < 5) c = Blend(c, (0, 0, 0), 0.55 * (1 - fromBottom / 5));
                if (sd > -1) c = Blend(c, (0, 0, 0), 0.7);

                buffer.Set(x, y, c.R, c.G, c.B, Math.Min(1, -sd / perPixel));
            }
        }
        return buffer;
    }

    /// <summary>The inner shadow along the top of a pad that is held down.</summary>
    public static PixelBuffer PadPressShade(int width, int height, double scale)
    {
        var buffer = new PixelBuffer(width, height);
        double lw = width / scale, lh = height / scale, perPixel = 1 / scale;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double lx = (x + 0.5) * perPixel, ly = (y + 0.5) * perPixel;
                double sd = RoundRect(lx, ly, lw, lh, PadRadius);
                if (sd >= 0) continue;
                double a = 0.72 * Math.Max(0, 1 - ly / 8);
                if (a > 0) buffer.Set(x, y, 0, 0, 0, a * Math.Min(1, -sd / perPixel));
            }
        }
        return buffer;
    }

    /// <summary>The glow a lit pad throws onto the plate around it. Size includes the overhang on each side.</summary>
    public static PixelBuffer PadBloom(int width, int height, double scale, PadTone tone)
    {
        var bloom = Parse(PadTones.Bloom(tone));
        var buffer = new PixelBuffer(width, height);
        double lw = width / scale, lh = height / scale, perPixel = 1 / scale;
        double reach = PadBloomOverhang;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double lx = (x + 0.5) * perPixel - reach, ly = (y + 0.5) * perPixel - reach;
                double sd = RoundRect(lx, ly, lw - reach * 2, lh - reach * 2, PadRadius + 2);
                if (sd <= 0 || sd >= reach) continue;
                buffer.Set(x, y, bloom.R, bloom.G, bloom.B, 0.62 * Gauss(sd, 11) * Sq(1 - sd / reach));
            }
        }
        return buffer;
    }

    /// <summary>The chrome collar of a push switch: light along its top, falling dark below, around a black opening.</summary>
    public static PixelBuffer SwitchBezel(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double per = SwitchBezelLogical / size, inset = (SwitchBezelLogical - SwitchOpening) / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5) * per, ly = (y + 0.5) * per;
                double outer = RoundRect(lx, ly, SwitchBezelLogical, SwitchBezelLogical, 6);
                if (outer >= 0) continue;

                double opening = RoundRect(lx - inset, ly - inset, SwitchOpening, SwitchOpening, 3.5);
                double v;
                if (opening < 0)
                {
                    v = 6;
                }
                else
                {
                    double t = (lx * 0.35 + ly * 0.93) / (SwitchBezelLogical * 1.28);
                    v = Mix(238, 70, t);
                    // A dark line where the collar meets the plate, and another at the lip of the opening.
                    if (outer > -0.7 || opening < 0.7) v *= 0.45;
                }
                buffer.Set(x, y, v, v * 1.005, v * 1.015, Math.Min(1, -outer / per));
            }
        }
        return buffer;
    }

    /// <summary>The cap of a push switch: brushed metal lit from above, with a slit that its LED shines through.</summary>
    public static PixelBuffer SwitchCap(int size)
    {
        var buffer = new PixelBuffer(size, size);
        var random = new Random(23);
        double per = SwitchCapLogical / size;
        var rows = new double[size];
        for (int y = 0; y < size; y++) rows[y] = (random.NextDouble() - 0.5) * 3;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5) * per, ly = (y + 0.5) * per;
                double sd = RoundRect(lx, ly, SwitchCapLogical, SwitchCapLogical, 4);
                if (sd >= 0) continue;

                double v = Mix(104, 44, ly / SwitchCapLogical) + rows[y];
                if (ly < 1.4) v += 42;
                if (ly > SwitchCapLogical - 1.6) v *= 0.55;
                if (sd > -0.8) v *= 0.6;

                double slit = SlitDistance(lx, ly);
                if (slit < 0) v = 6;
                else if (slit < 0.8 && ly > 8) v += 26;

                buffer.Set(x, y, v, v * 1.01, v * 1.03, Math.Min(1, -sd / per));
            }
        }
        return buffer;
    }

    /// <summary>The cap's slit lit up, with the glow it throws across the metal. Sits over the cap and moves with it.</summary>
    public static PixelBuffer SwitchLed(int size, LedColor color)
    {
        var core = Parse(LedColors.Core(color));
        var hot = Blend(core, (255, 255, 255), 0.55);
        var buffer = new PixelBuffer(size, size);
        double per = SwitchCapLogical / size;
        const double Reach = 6;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5) * per, ly = (y + 0.5) * per;
                double slit = SlitDistance(lx, ly);
                if (slit >= Reach) continue;

                if (slit < 0)
                {
                    var c = Blend(core, hot, Math.Min(1, -slit / 1.6));
                    buffer.Set(x, y, c.R, c.G, c.B);
                }
                else
                {
                    double a = (0.75 * Gauss(slit, 2.2) + 0.35 * Gauss(slit, 6)) * Sq(1 - slit / Reach);
                    buffer.Set(x, y, core.R, core.G, core.B, a);
                }
            }
        }
        return buffer;
    }

    /// <summary>The inner shadow along the top of a cap that has been pushed in.</summary>
    public static PixelBuffer SwitchCapShade(int size)
    {
        var buffer = new PixelBuffer(size, size);
        double per = SwitchCapLogical / size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double lx = (x + 0.5) * per, ly = (y + 0.5) * per;
                double sd = RoundRect(lx, ly, SwitchCapLogical, SwitchCapLogical, 4);
                if (sd >= 0) continue;
                double a = 0.72 * Math.Max(0, 1 - ly / 8);
                if (a > 0) buffer.Set(x, y, 0, 0, 0, a * Math.Min(1, -sd / per));
            }
        }
        return buffer;
    }

    /// <summary>Distance to the LED slit on a cap, negative inside. The slit is 18 by 4, centered, 6 from the top.</summary>
    static double SlitDistance(double lx, double ly) => RoundRect(lx - 10, ly - 6, 18, 4, 1.5);

    static double Coverage(int x, int y, double perX, double perY, (double X, double Y)[] points)
    {
        const int Samples = 3;
        int inside = 0;
        for (int sy = 0; sy < Samples; sy++)
        {
            for (int sx = 0; sx < Samples; sx++)
            {
                double lx = (x + (sx + 0.5) / Samples) * perX - DigitGlow;
                double ly = (y + (sy + 0.5) / Samples) * perY - DigitGlow;
                if (InPolygon(lx, ly, points)) inside++;
            }
        }
        return inside / (double)(Samples * Samples);
    }

    static bool InPolygon(double x, double y, (double X, double Y)[] points)
    {
        bool inside = false;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
        {
            if ((points[i].Y > y) != (points[j].Y > y)
                && x < (points[j].X - points[i].X) * (y - points[i].Y) / (points[j].Y - points[i].Y) + points[i].X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Three box passes approximate a gaussian, and keep the glow inside the art's margin.</summary>
    static double[] BoxBlur(double[] source, int width, int height, int radius)
    {
        var current = (double[])source.Clone();
        for (int pass = 0; pass < 3; pass++)
        {
            var horizontal = new double[current.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double sum = 0;
                    for (int k = Math.Max(0, x - radius); k <= Math.Min(width - 1, x + radius); k++) sum += current[y * width + k];
                    horizontal[y * width + x] = sum / (radius * 2 + 1);
                }
            }
            var vertical = new double[current.Length];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    double sum = 0;
                    for (int k = Math.Max(0, y - radius); k <= Math.Min(height - 1, y + radius); k++) sum += horizontal[k * width + x];
                    vertical[y * width + x] = sum / (radius * 2 + 1);
                }
            }
            current = vertical;
        }
        return current;
    }

    /// <summary>Lays a straight-alpha color over a premultiplied one.</summary>
    static (double A, double R, double G, double B) Over(double a, double r, double g, double b, double topAlpha, double topR, double topG, double topB) =>
        (topAlpha + a * (1 - topAlpha), topR * topAlpha + r * (1 - topAlpha), topG * topAlpha + g * (1 - topAlpha), topB * topAlpha + b * (1 - topAlpha));

    /// <summary>Signed distance to a rounded rectangle whose top left corner is at the origin. Negative is inside.</summary>
    static double RoundRect(double x, double y, double width, double height, double radius)
    {
        double qx = Math.Abs(x - width / 2) - (width / 2 - radius), qy = Math.Abs(y - height / 2) - (height / 2 - radius);
        return Math.Min(Math.Max(qx, qy), 0) + Math.Sqrt(Sq(Math.Max(qx, 0)) + Sq(Math.Max(qy, 0))) - radius;
    }

    static double Gauss(double x, double width) => Math.Exp(-(x / width) * (x / width));

    static double Sq(double v) => v * v;

    static double Smooth(double from, double to, double v)
    {
        double t = Math.Clamp((v - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static double Mix(double a, double b, double t) => a + (b - a) * t;

    static (double R, double G, double B) Blend((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));

    static (double R, double G, double B) Parse(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
