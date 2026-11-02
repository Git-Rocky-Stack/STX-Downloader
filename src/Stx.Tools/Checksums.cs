using System.Text.RegularExpressions;

namespace Stx.Tools;

/// <summary>Reads the checksum files tools publish. Each project writes them a little differently.</summary>
public static partial class Checksums
{
    /// <summary>The hash for one file in a "hash  name" list, or null.</summary>
    public static string? Find(string text, string fileName)
    {
        foreach (string line in text.Split('\n'))
        {
            var match = Line().Match(line.Trim());
            if (match.Success && match.Groups["name"].Value == fileName) return match.Groups["hash"].Value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>The hash from a file that holds just one, whatever surrounds it, or null.</summary>
    public static string? FindOnly(string text)
    {
        var match = AnyHash().Match(text);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^(?<hash>[0-9a-fA-F]{64})\s+\*?(?<name>.+)$")]
    private static partial Regex Line();

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b")]
    private static partial Regex AnyHash();
}
