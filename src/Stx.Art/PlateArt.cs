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
        double sheenAmp = dark ? 14 : 16;
        double chamferLight = dark ? 46 : 30, chamferDark = dark ? 26 : 74;

        var buffer = new PixelBuffer(width, height);
        var random = new Random(11);
        var noise = new double[width + GrainWindow];
        var grainRow = new double[width];

        for (int y = 0; y < height; y++)
        {
            double rowOffset = GrainRow(random, noise, grainRow, dark, wrap: false);
            double t = height > 1 ? (double)y / (height - 1) : 0;
            double baseTone = t < 0.26 ? top + (mid - top) * t / 0.26 : mid + (bottom - mid) * (t - 0.26) / 0.74;

            for (int x = 0; x < width; x++)
            {
                double grain = grainRow[x];
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

    /// <summary>
    /// A seamless horizontal grain tile for the project site: neutral gray 128 plus the plate's own grain and per-row offset times <paramref name="gain"/>.
    /// No gradient, sheen or chamfer, and the window wraps so the right edge meets the left.
    /// </summary>
    public static PixelBuffer GrainTile(int width, int height, LookMode mode, double gain)
    {
        bool dark = mode == LookMode.Dark;
        var buffer = new PixelBuffer(width, height);
        var random = new Random(11);
        var noise = new double[width];
        var grainRow = new double[width];

        for (int y = 0; y < height; y++)
        {
            double rowOffset = GrainRow(random, noise, grainRow, dark, wrap: true);
            for (int x = 0; x < width; x++)
            {
                double v = 128 + (grainRow[x] + rowOffset) * gain;
                buffer.Set(x, y, v, v, v);
            }
        }
        return buffer;
    }

    /// <summary>
    /// One row of grain: noise blurred along x over <see cref="GrainWindow"/> samples and normalised, written to <paramref name="grain"/>; returns the row's offset.
    /// Both <see cref="Render"/> and <see cref="GrainTile"/> draw their noise here. Without <paramref name="wrap"/> the noise buffer is
    /// width + window long; with it the buffer is width long and the window reads it modulo width.
    /// </summary>
    static double GrainRow(Random random, double[] noise, double[] grain, bool dark, bool wrap)
    {
        double grainAmp = dark ? 2 : 2.2, rowAmp = dark ? 1 : 1.2;
        double norm = Math.Sqrt(GrainWindow / 12.0);
        int width = grain.Length;

        double sum = 0;
        for (int i = 0; i < noise.Length; i++) noise[i] = random.NextDouble() - 0.5;
        for (int i = 0; i < GrainWindow; i++) sum += noise[wrap ? i % width : i];

        for (int x = 0; x < width; x++)
        {
            grain[x] = sum / norm * grainAmp;
            sum += noise[wrap ? (x + GrainWindow) % width : x + GrainWindow] - noise[x];
        }
        return (random.NextDouble() - 0.5) * rowAmp;
    }

    static double Sq(double v) => v * v;

    static double Gray(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16) + Convert.ToInt32(hex.Substring(3, 2), 16) + Convert.ToInt32(hex.Substring(5, 2), 16)) / 3.0;
}
