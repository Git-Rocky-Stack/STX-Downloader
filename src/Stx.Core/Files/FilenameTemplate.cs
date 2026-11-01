using System.Globalization;
using System.Text.RegularExpressions;

namespace Stx.Core.Files;

public sealed record MediaInfo(string? Title, string? Id, string? Uploader, string? Date, int? Height, string? Format);

/// <summary>Fills in a naming pattern. An unknown {token} stays as typed, so a typo shows up in the name and never stops a job.</summary>
public static partial class FilenameTemplate
{
    public const string Default = "{title} [{id}]";

    public static string Render(string template, MediaInfo info)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return Token().Replace(template, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "title" => info.Title ?? "",
            "id" => info.Id ?? "",
            "artist" => info.Uploader ?? "",
            "date" => info.Date ?? "",
            "height" => info.Height?.ToString(CultureInfo.InvariantCulture) ?? "",
            "format" => info.Format ?? "",
            _ => match.Value,
        });
    }

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Token();
}
