namespace Stx.Look;

public readonly record struct ScrewSpot(double X, double Y);

/// <summary>Every panel gets four screws. A panel cannot opt out.</summary>
public static class ScrewLayout
{
    public const double Size = 18;
    public const double Inset = 6;

    public static IReadOnlyList<ScrewSpot> For(double width, double height)
    {
        double far = Inset + Size;
        return
        [
            new(Inset, Inset),
            new(width - far, Inset),
            new(Inset, height - far),
            new(width - far, height - far),
        ];
    }

    /// <summary>Each screw seats at its own angle. Stable per panel name, so screenshot baselines hold.</summary>
    public static IReadOnlyList<int> AnglesFor(string panelName)
    {
        uint hash = 2166136261;
        foreach (char c in panelName) hash = (hash ^ c) * 16777619;

        var angles = new int[4];
        for (int i = 0; i < 4; i++)
        {
            hash = (hash ^ (uint)i) * 16777619;
            angles[i] = (int)(hash % 60);
        }
        return angles;
    }
}
