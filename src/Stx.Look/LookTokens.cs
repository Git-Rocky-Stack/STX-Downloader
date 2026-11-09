namespace Stx.Look;

/// <summary>Color values for one mode. Every primitive reads color from here, so both modes stay complete.</summary>
public sealed class LookTokens
{
    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "PlateTop", "PlateMid", "PlateBottom", "Ink", "InkDim", "EdgeHighlight", "EdgeShadow", "Chassis", "EngraveHigh", "EngraveLow", "Accent",
    ];

    static readonly LookTokens Dark = new(new Dictionary<string, string>
    {
        ["PlateTop"] = "#3A3D41",
        ["PlateMid"] = "#2A2C2F",
        ["PlateBottom"] = "#1D1F22",
        ["Ink"] = "#DFE3E8",
        ["InkDim"] = "#A4ABB3",
        ["EdgeHighlight"] = "#33FFFFFF",
        ["EdgeShadow"] = "#CC000000",
        ["Chassis"] = "#08090A",
        ["EngraveHigh"] = "#24FFFFFF",
        ["EngraveLow"] = "#D9000000",
        // Names of pads and switches in running text, such as the user guide's: the displays' cyan, lifted to read on metal.
        ["Accent"] = "#7FD4FF",
    });

    static readonly LookTokens Light = new(new Dictionary<string, string>
    {
        ["PlateTop"] = "#E0E2E5",
        ["PlateMid"] = "#C4C7CB",
        ["PlateBottom"] = "#A6AAAF",
        ["Ink"] = "#121417",
        ["InkDim"] = "#33383E",
        ["EdgeHighlight"] = "#D9FFFFFF",
        ["EdgeShadow"] = "#8C000000",
        ["Chassis"] = "#26282B",
        ["EngraveHigh"] = "#47000000",
        ["EngraveLow"] = "#B3FFFFFF",
        ["Accent"] = "#073457",
    });

    readonly IReadOnlyDictionary<string, string> _values;

    LookTokens(IReadOnlyDictionary<string, string> values) => _values = values;

    public string this[string key] => _values.TryGetValue(key, out var v) ? v : "";

    public static LookTokens For(LookMode mode) => mode == LookMode.Dark ? Dark : Light;

    /// <summary>WCAG contrast ratio between two opaque #RRGGBB colors.</summary>
    public static double Contrast(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static double Luminance(string hex)
    {
        static double Channel(string h, int i)
        {
            double c = Convert.ToInt32(h.Substring(i, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
    }
}
