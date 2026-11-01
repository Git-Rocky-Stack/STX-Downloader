using System.Text.Json;

namespace Stx.Tools;

public sealed record PlaylistEntry(string Id, string? Title, string? Url, bool Available);

/// <summary>Reads yt-dlp's JSON listing. Private, deleted and members-only entries are marked, so the rest can carry on.</summary>
public static class PlaylistListing
{
    static readonly string[] GoneTitles = ["[Private video]", "[Deleted video]"];
    static readonly string[] GoneAvailability = ["private", "premium_only", "subscriber_only", "needs_auth"];

    public static IReadOnlyList<PlaylistEntry> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return [];
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return Entry(root) is { } single ? [single] : [];

            var list = new List<PlaylistEntry>();
            foreach (var item in entries.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && Entry(item) is { } entry) list.Add(entry);
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The list's own title, or the video's, or null.</summary>
    public static string? ParseTitle(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? Text(document.RootElement, "title") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static PlaylistEntry? Entry(JsonElement item)
    {
        string? id = Text(item, "id");
        if (id is null) return null;
        string? title = Text(item, "title");
        bool available = !GoneTitles.Contains(title) && !GoneAvailability.Contains(Text(item, "availability"));
        return new PlaylistEntry(id, title, Text(item, "url") ?? Text(item, "webpage_url"), available);
    }

    static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
