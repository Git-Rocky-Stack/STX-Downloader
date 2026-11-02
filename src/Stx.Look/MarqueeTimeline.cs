namespace Stx.Look;

/// <summary>
/// How a line too long for its display scrolls: hold at the start, travel to the end, hold, jump back.
/// A job that's working scrolls for as long as it works. One you're looking at scrolls a few passes and then rests,
/// because a finished row left selected must not keep an idle app busy.
/// </summary>
public static class MarqueeTimeline
{
    public const double PixelsPerSecond = 40;
    public const double HoldSeconds = 1.5;
    public const int PassesWhileLookedAt = 3;

    /// <summary>Under a character's width isn't worth moving for.</summary>
    const double SmallestOverflow = 4;

    public static bool ShouldScroll(double overflow, bool working, bool lookedAt, bool reduceMotion) =>
        overflow >= SmallestOverflow && !reduceMotion && (working || lookedAt);

    /// <summary>One whole pass: both holds and the trip across.</summary>
    public static double PassSeconds(double overflow) => HoldSeconds * 2 + overflow / PixelsPerSecond;

    /// <summary>Where each part of a pass ends, from 0 to 1. The start holds until the first, and the end holds until the second.</summary>
    public static (double TravelStarts, double TravelEnds) Fractions(double overflow)
    {
        double pass = PassSeconds(overflow);
        return (HoldSeconds / pass, (pass - HoldSeconds) / pass);
    }
}
