using System.Globalization;
using System.Text.RegularExpressions;

namespace Stx.Audio;

public readonly record struct Loudness(double? Lufs, double? TruePeakDb);

/// <summary>Reads the closing summary of FFmpeg's ebur128 filter. No summary means no value, and never a guess.</summary>
public static partial class LoudnessParser
{
    public static Loudness Parse(string text)
    {
        int summary = (text ?? "").LastIndexOf("Integrated loudness:", StringComparison.Ordinal);
        if (summary < 0) return new Loudness(null, null);
        string tail = text![summary..];
        return new Loudness(Read(Integrated().Match(tail)), Read(TruePeak().Match(tail)));
    }

    static double? Read(Match match) =>
        match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;

    [GeneratedRegex(@"I:\s+(-?\d+(?:\.\d+)?)\s+LUFS")]
    private static partial Regex Integrated();

    [GeneratedRegex(@"Peak:\s+(-?\d+(?:\.\d+)?)\s+dBFS")]
    private static partial Regex TruePeak();
}
