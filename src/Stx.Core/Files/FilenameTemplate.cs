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

    /// <summary>The same template in yt-dlp's own terms, so downloads are named as you set them.</summary>
    public static string ToYtDlp(string template)
    {
        string source = string.IsNullOrWhiteSpace(template) ? Default : template;
        // Only a name, never a path: yt-dlp's -P option alone decides the folder.
        string flat = source.Replace("..", "").Replace('\\', ' ').Replace('/', ' ').Replace("%", "%%").Trim();
        string fields = Token().Replace(flat, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "title" => "%(title)s",
            "id" => "%(id)s",
            "artist" => "%(uploader)s",
            "date" => "%(upload_date)s",
            "height" => "%(height)s",
            "format" => "%(ext)s",
            _ => match.Value,
        });
        return fields + ".%(ext)s";
    }

    /// <summary>
    /// A captured stream arrives with no name of its own, so yt-dlp would call every one "master". This fills in the
    /// name the page gave it and uses the library's number as its id. Anything else in your template still works.
    /// </summary>
    public static string ForCaptured(string template, string? title, long mediaId)
    {
        string source = string.IsNullOrWhiteSpace(template) ? Default : template;
        string safe = (title ?? "").Replace("{", "").Replace("}", "").Trim();
        return Token().Replace(source, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "title" when safe.Length > 0 => safe,
            "id" => mediaId.ToString(CultureInfo.InvariantCulture),
            _ => match.Value,
        });
    }

    /// <summary>
    /// A name that two different videos can't share, for when this template's name is already taken. Null when the
    /// template already has {id}, because then the name can only be taken by the same video.
    /// </summary>
    public static string? UniqueVariant(string template)
    {
        string source = string.IsNullOrWhiteSpace(template) ? Default : template;
        bool hasId = Token().Matches(source).Any(m => m.Groups[1].Value.Equals("id", StringComparison.OrdinalIgnoreCase));
        return hasId ? null : source.TrimEnd() + " [{id}]";
    }

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Token();
}
