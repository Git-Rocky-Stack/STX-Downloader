using System.Globalization;
using System.Text.RegularExpressions;

namespace Stx.Tools;

public readonly record struct Progress(double Percent, string? Size, string? Speed, string? Eta);

/// <summary>Reads yt-dlp's progress lines, such as "[download]  13.6% of   29.34MiB at    4.38MiB/s ETA 00:05".</summary>
public static partial class YtDlpProgress
{
    public static bool TryParse(string line, out Progress progress)
    {
        progress = default;
        var match = Line().Match(line ?? "");
        if (!match.Success) return false;

        progress = new Progress(
            double.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture),
            Known(match.Groups["size"].Value),
            Known(match.Groups["speed"].Value),
            Known(match.Groups["eta"].Value));
        return true;
    }

    static string? Known(string value) => value.Length == 0 || value.StartsWith("Unknown", StringComparison.Ordinal) ? null : value;

    [GeneratedRegex(@"^\[download\]\s+(?<percent>\d+(?:\.\d+)?)%(?:\s+of\s+~?\s*(?<size>\S+))?(?:\s+in\s+\S+)?(?:\s+at\s+(?<speed>Unknown B/s|\S+))?(?:\s+ETA\s+(?<eta>\S+))?")]
    private static partial Regex Line();
}
