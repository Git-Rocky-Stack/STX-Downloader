namespace Stx.Look;

[Flags]
public enum Segments
{
    None = 0,
    A = 1,
    B = 2,
    C = 4,
    D = 8,
    E = 16,
    F = 32,
    G = 64,
}

/// <summary>Which bars light for a character. Anything without a shape lights nothing.</summary>
public static class SevenSegment
{
    public static Segments For(char c) => c switch
    {
        '0' => Segments.A | Segments.B | Segments.C | Segments.D | Segments.E | Segments.F,
        '1' => Segments.B | Segments.C,
        '2' => Segments.A | Segments.B | Segments.G | Segments.E | Segments.D,
        '3' => Segments.A | Segments.B | Segments.G | Segments.C | Segments.D,
        '4' => Segments.F | Segments.G | Segments.B | Segments.C,
        '5' => Segments.A | Segments.F | Segments.G | Segments.C | Segments.D,
        '6' => Segments.A | Segments.F | Segments.G | Segments.E | Segments.D | Segments.C,
        '7' => Segments.A | Segments.B | Segments.C,
        '8' => Segments.A | Segments.B | Segments.C | Segments.D | Segments.E | Segments.F | Segments.G,
        '9' => Segments.A | Segments.B | Segments.C | Segments.D | Segments.F | Segments.G,
        _ => Segments.None,
    };
}
