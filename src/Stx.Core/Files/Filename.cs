using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Stx.Core.Files;

/// <summary>Turns any title into a path that's legal on Windows, stays in its folder, fits, and never lands on an existing file.</summary>
public static partial class Filename
{
    /// <summary>Under Windows' old 260 limit, leaving room for the tools' temporary suffixes.</summary>
    public const int MaxPathLength = 240;

    const string Fallback = "Untitled";
    const int ShortestStem = 16;

    public static string Safe(string title, string ext, string folder, Func<string, bool> exists, string? sourceId = null)
    {
        string stem = Clean(title);
        if (stem.Length == 0) stem = Clean(sourceId ?? "");
        if (stem.Length == 0) stem = Fallback;

        string path = Fit(stem, "", ext, folder);
        for (int n = 2; exists(path); n++) path = Fit(stem, $" ({n})", ext, folder);
        return path;
    }

    static string Fit(string stem, string suffix, string ext, string folder)
    {
        int room = Math.Max(ShortestStem, MaxPathLength - folder.Length - 1 - ext.Length - suffix.Length);
        return Path.Combine(folder, Cut(stem, room) + suffix + ext);
    }

    static string Clean(string? title)
    {
        var text = new StringBuilder();
        foreach (char c in title ?? "")
            if (!char.IsControl(c) && !"<>:\"/\\|?*".Contains(c)) text.Append(c);

        string clean = text.ToString().Trim().TrimEnd('.', ' ');
        // Windows treats CON.anything as the device, so any name that begins that way gets a prefix.
        return Reserved().IsMatch(clean) ? "_" + clean : clean;
    }

    // Cuts between whole characters: emoji and combining marks span several chars, and half of one is invalid.
    static string Cut(string stem, int room)
    {
        if (stem.Length <= room) return stem;
        var kept = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(stem);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (kept.Length + element.Length > room) break;
            kept.Append(element);
        }
        return kept.ToString().TrimEnd('.', ' ');
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Reserved();
}
