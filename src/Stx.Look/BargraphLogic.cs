namespace Stx.Look;

public enum BargraphZones { Progress, Vu }

/// <summary>Which segments of an LED bargraph are lit. Values are percentages, and anything unusable reads as nothing.</summary>
public static class BargraphLogic
{
    public static int LitCount(double value, int segments)
    {
        if (!double.IsFinite(value)) return 0;
        return Math.Clamp((int)Math.Floor(value * segments / 100 + 1e-9), 0, segments);
    }

    /// <summary>The one segment holding the peak, or -1 for none.</summary>
    public static int PeakIndex(double peak, int segments)
    {
        if (!double.IsFinite(peak) || peak <= 0) return -1;
        return Math.Clamp((int)Math.Round(peak * segments / 100, MidpointRounding.AwayFromZero) - 1, -1, segments - 1);
    }

    /// <summary>
    /// The family a segment's color belongs to: what its zone means. A progress meter is all one family (blue: work
    /// moving, nothing to warn about). A level meter is green to 72%, amber to 90%, red above: the warning zones.
    /// </summary>
    public static LedColor ZoneColor(int index, int segments, BargraphZones zones)
    {
        if (zones == BargraphZones.Progress) return LedColor.Blue;
        int percentTimesSegments = (index + 1) * 100;
        if (percentTimesSegments > 90 * segments) return LedColor.Red;
        return percentTimesSegments > 72 * segments ? LedColor.Amber : LedColor.Green;
    }

    /// <summary>
    /// How many distinct colors a ramp is cut into. Each color is one lit bitmap shared by every segment that uses it,
    /// so a meter costs at most this many bitmaps per state however many segments it has, and neighbors can share.
    /// </summary>
    public const int RampSteps = 16;

    // Progress, "aurora": aqua through the console's cyan and azure to violet, like a fluorescent spectrum display.
    // Nothing on it warns, so no stop is amber or red.
    static readonly (double At, string Hex)[] Aurora =
    [
        (0.00, "#22E6B4"), (0.30, "#49C8FF"), (0.65, "#4F7DFF"), (1.00, "#B06BFF"),
    ];

    // Level: green through lime, yellow through orange, then red. Each warning zone keeps its family (ZoneColor), so a
    // reading means what it always meant; only the drawing is finer.
    static readonly (double At, string Hex)[] GreenRamp = [(0.00, "#2FF07A"), (1.00, "#B8FF3B")];
    static readonly (double At, string Hex)[] AmberRamp = [(0.00, "#FFE03B"), (1.00, "#FF9A1F")];
    static readonly (double At, string Hex)[] RedRamp = [(0.00, "#FF5A36"), (1.00, "#FF2E4D")];

    /// <summary>
    /// A segment's own color, as #RRGGBB: the meter's ramp at that segment, cut into <see cref="RampSteps"/> colors.
    /// Always inside its family (<see cref="ZoneColor"/>).
    /// </summary>
    public static string ZoneHex(int index, int segments, BargraphZones zones)
    {
        segments = Math.Max(1, segments);
        index = Math.Clamp(index, 0, segments - 1);
        if (zones == BargraphZones.Progress) return Sample(Aurora, segments == 1 ? 0 : index / (double)(segments - 1));

        // A level meter: each segment placed within its own zone, so green never shades into amber before 72%.
        double percent = (index + 1) * 100.0 / segments;
        return ZoneColor(index, segments, zones) switch
        {
            LedColor.Red => Sample(RedRamp, Within(percent, 90, 100)),
            LedColor.Amber => Sample(AmberRamp, Within(percent, 72, 90)),
            _ => Sample(GreenRamp, Within(percent, 0, 72)),
        };
    }

    static double Within(double percent, double from, double to) => Math.Clamp((percent - from) / (to - from), 0, 1);

    static string Sample((double At, string Hex)[] ramp, double t)
    {
        t = Math.Round(Math.Clamp(t, 0, 1) * (RampSteps - 1)) / (RampSteps - 1);
        int i = 0;
        while (i < ramp.Length - 2 && t > ramp[i + 1].At) i++;
        var (a, b) = (ramp[i], ramp[i + 1]);
        double u = b.At > a.At ? Math.Clamp((t - a.At) / (b.At - a.At), 0, 1) : 0;
        var (r1, g1, b1) = Rgb(a.Hex);
        var (r2, g2, b2) = Rgb(b.Hex);
        return $"#{Channel(r1, r2, u):X2}{Channel(g1, g2, u):X2}{Channel(b1, b2, u):X2}";
    }

    static int Channel(int from, int to, double u) => (int)Math.Round(from + (to - from) * u);

    static (int R, int G, int B) Rgb(string hex) =>
        (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));

    /// <summary>Segments that differ between two readings, each once, in order. Everything else is left alone.</summary>
    public static IReadOnlyList<int> Changed(int oldLit, int newLit, int oldPeak, int newPeak)
    {
        var changed = new SortedSet<int>();
        for (int i = Math.Min(oldLit, newLit); i < Math.Max(oldLit, newLit); i++) changed.Add(i);
        if (oldPeak != newPeak)
        {
            if (oldPeak >= 0) changed.Add(oldPeak);
            if (newPeak >= 0) changed.Add(newPeak);
        }
        return [.. changed];
    }
}
