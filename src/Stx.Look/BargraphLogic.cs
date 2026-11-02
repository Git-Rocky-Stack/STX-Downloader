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

    public static LedColor ZoneColor(int index, int segments, BargraphZones zones)
    {
        if (zones == BargraphZones.Progress) return LedColor.Green;
        int percentTimesSegments = (index + 1) * 100;
        if (percentTimesSegments > 90 * segments) return LedColor.Red;
        return percentTimesSegments > 72 * segments ? LedColor.Amber : LedColor.Green;
    }

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
