namespace Stx.Audio;

/// <summary>What the meters show at one moment. Values are percent of the scale, and a NaN peak shows no peak segment.</summary>
public readonly record struct MeterFrame(double Left, double Right, double PeakLeft, double PeakRight);

/// <summary>
/// Meters show real data only. Dark with nothing selected, holding the measured peaks at rest, and for a few
/// seconds after analysis, sweeping through the levels actually measured. Every value comes from the file.
/// </summary>
public static class MeterTimeline
{
    public const int FramesPerSecond = 20;

    const int SweepFrames = 4 * FramesPerSecond;
    const double ScaleBottomDb = -60;

    public static MeterFrame Dark { get; } = new(0, 0, double.NaN, double.NaN);

    /// <summary>Empty when motion is reduced, or when nothing has been measured yet.</summary>
    public static IReadOnlyList<MeterFrame> Sweep(AudioFacts facts, bool reduceMotion = false)
    {
        if (reduceMotion || !facts.MeasuredFromFile || facts.Envelope.Count == 0) return [];

        var hold = Hold(facts);
        var frames = new MeterFrame[SweepFrames];
        for (int i = 0; i < SweepFrames; i++)
        {
            var point = facts.Envelope[i * (facts.Envelope.Count - 1) / (SweepFrames - 1)];
            frames[i] = new MeterFrame(ToPercent(point.LeftDb), ToPercent(point.RightDb), hold.PeakLeft, hold.PeakRight);
        }
        return frames;
    }

    public static MeterFrame Hold(AudioFacts facts)
    {
        if (!facts.MeasuredFromFile || facts.PeakLeftDb is not { } left || facts.PeakRightDb is not { } right) return Dark;
        double l = ToPercent(left), r = ToPercent(right);
        return new MeterFrame(l, r, l > 0 ? l : double.NaN, r > 0 ? r : double.NaN);
    }

    public static double ToPercent(double db) =>
        double.IsNegativeInfinity(db) ? 0 : Math.Clamp((db - ScaleBottomDb) / -ScaleBottomDb * 100, 0, 100);
}
