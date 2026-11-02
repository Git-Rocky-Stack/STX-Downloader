using Stx.Look;

namespace Stx.Art;

/// <summary>Draws a measured spectrum as bars on a log scale, with a marker at the measured roll-off. An empty spectrum draws nothing.</summary>
public static class SpectrumArt
{
    const double RangeDb = 80;
    const int BarGap = 1;
    const double MarkerAlpha = 0.95;

    public static PixelBuffer Render(int width, int height, IReadOnlyList<double> hz, IReadOnlyList<double> db, double? rollOffHz, DisplayTone tone)
    {
        var buffer = new PixelBuffer(width, height);
        if (hz.Count == 0 || db.Count != hz.Count) return buffer;

        var bar = Parse(DisplayTones.Text(tone, dimmed: false));
        double top = db.Max();
        int count = hz.Count;
        for (int band = 0; band < count; band++)
        {
            int left = band * width / count, right = (band + 1) * width / count - BarGap;
            double level = Math.Clamp((db[band] - (top - RangeDb)) / RangeDb, 0, 1);
            int bars = (int)Math.Round(level * height);
            for (int x = left; x < right; x++)
            {
                for (int y = height - bars; y < height; y++)
                {
                    double t = (height - y) / (double)height;
                    buffer.Set(x, y, bar.R, bar.G, bar.B, 0.45 + 0.45 * t);
                }
            }
        }

        if (rollOffHz is { } cutoff && cutoff < hz[^1])
        {
            var marker = Parse(tone == DisplayTone.Cyan ? DisplayTones.Text(DisplayTone.Amber, false) : DisplayTones.Text(DisplayTone.Cyan, false));
            int at = XFor(cutoff, hz, width);
            for (int y = 0; y < height; y++)
                for (int x = at; x < Math.Min(width, at + 2); x++) buffer.Set(x, y, marker.R, marker.G, marker.B, MarkerAlpha);
        }
        return buffer;
    }

    /// <summary>Where a frequency falls across the width.</summary>
    public static int XFor(double frequency, IReadOnlyList<double> hz, int width)
    {
        double low = Math.Log(hz[0]), high = Math.Log(hz[^1]);
        double t = high > low ? (Math.Log(Math.Max(frequency, hz[0])) - low) / (high - low) : 0;
        return Math.Clamp((int)(t * (width - 1)), 0, width - 1);
    }

    static (double R, double G, double B) Parse(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
}
