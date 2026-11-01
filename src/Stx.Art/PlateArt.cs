using Stx.Look;

namespace Stx.Art;

/// <summary>Brushed aluminium: horizontal grain from noise blurred along x, a chamfered edge, and a soft sheen from the top left.</summary>
public static class PlateArt
{
    const int GrainWindow = 96;

    public static PixelBuffer Render(int width, int height, LookMode mode)
    {
        var tokens = LookTokens.For(mode);
        bool dark = mode == LookMode.Dark;
        double top = Gray(tokens["PlateTop"]), mid = Gray(tokens["PlateMid"]), bottom = Gray(tokens["PlateBottom"]);
        double grainAmp = dark ? 2 : 2.2, rowAmp = dark ? 1 : 1.2, sheenAmp = dark ? 14 : 16;
        double chamferLight = dark ? 46 : 30, chamferDark = dark ? 26 : 74;

        var buffer = new PixelBuffer(width, height);
        var random = new Random(11);
        var noise = new double[width + GrainWindow];
        double norm = Math.Sqrt(GrainWindow / 12.0);

        for (int y = 0; y < height; y++)
        {
            double sum = 0;
            for (int i = 0; i < noise.Length; i++)
            {
                noise[i] = random.NextDouble() - 0.5;
                if (i < GrainWindow) sum += noise[i];
            }
            double t = height > 1 ? (double)y / (height - 1) : 0;
            double baseTone = t < 0.26 ? top + (mid - top) * t / 0.26 : mid + (bottom - mid) * (t - 0.26) / 0.74;
            double rowOffset = (random.NextDouble() - 0.5) * rowAmp;

            for (int x = 0; x < width; x++)
            {
                double grain = sum / norm * grainAmp;
                sum += noise[x + GrainWindow] - noise[x];
                double sheen = sheenAmp * Math.Exp(-(Sq((x - width * 0.32) / (width * 0.42)) + Sq((y - height * 0.15) / (height * 0.7))));
                double v = baseTone + grain + rowOffset + sheen;

                if (y < 2) v += chamferLight;
                else if (y >= height - 2) v -= chamferDark;
                if (x < 1) v += chamferLight * 0.3;
                else if (x >= width - 1) v -= chamferDark * 0.5;

                buffer.Set(x, y, v, v * 1.004, v * 1.012);
            }
        }
        return buffer;
    }

    static double Sq(double v) => v * v;

    static double Gray(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16) + Convert.ToInt32(hex.Substring(3, 2), 16) + Convert.ToInt32(hex.Substring(5, 2), 16)) / 3.0;
}
