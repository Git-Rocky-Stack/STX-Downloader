namespace Stx.Look;

/// <summary>How an LED behaves as a job starts: it strikes with three flashes, settles, then blinks while busy. Only brightness moves.</summary>
public static class StrikeTimeline
{
    public const double PeakBrightness = 2.6;
    public const double BusySeconds = 1.15;

    public static IReadOnlyList<(double T, double Brightness)> Default { get; } =
    [
        (0, 0.1),
        (0.066, 2.6),
        (0.154, 0.15),
        (0.264, 2.3),
        (0.352, 0.2),
        (0.462, 2.1),
        (0.616, 1),
        (1.1, 1),
    ];

    public static IReadOnlyList<(double T, double Brightness)> Busy { get; } =
    [
        (0, 1.12),
        (0.667, 1.12),
        (0.678, 0.34),
        (BusySeconds, 0.34),
    ];

    /// <summary>Bitmaps cannot exceed full brightness, so anything above 1 fades in a second, hotter layer.</summary>
    public static (double Lit, double Flare) Split(double brightness) =>
        (Math.Clamp(brightness, 0, 1), Math.Clamp((brightness - 1) / (PeakBrightness - 1), 0, 1));

    /// <summary>The brightness to hold without animating, or null when this state should animate. Reduced motion makes every LED hold still.</summary>
    public static double? SteadyBrightness(LedState state, bool reduceMotion) => state switch
    {
        LedState.Off => 0,
        LedState.On => 1,
        _ => reduceMotion ? 1 : null,
    };

    public static LedState After(LedState state) => state == LedState.Strike ? LedState.Busy : state;
}
