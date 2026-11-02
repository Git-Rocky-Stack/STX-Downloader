using System.Globalization;
using System.Text.Json;

namespace Stx.Audio;

public sealed record ProbeFacts(string Codec, int? BitrateKbps, int? SampleRate, int? Channels, double? DurationSeconds, bool HasCoverArt);

/// <summary>Reads ffprobe's JSON. A file with no audio stream gives null, because there's nothing to report on.</summary>
public static class ProbeParser
{
    public static ProbeFacts? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                return null;

            JsonElement? audio = null;
            bool cover = false, video = false;
            foreach (var stream in streams.EnumerateArray())
            {
                string? type = Text(stream, "codec_type");
                if (type == "audio") audio ??= stream;
                else if (type == "video")
                {
                    if (IsAttachedPicture(stream)) cover = true;
                    else video = true;
                }
            }
            if (audio is not { } a) return null;

            root.TryGetProperty("format", out var format);
            long? bits = Number(a, "bit_rate") ?? Tag(a, "BPS");
            // A whole-file bitrate only describes the audio when audio is all that's in the file.
            if (bits is null && !video && format.ValueKind == JsonValueKind.Object) bits = Number(format, "bit_rate");

            return new ProbeFacts(
                Text(a, "codec_name") ?? "unknown",
                bits is long b ? (int)(b / 1000) : null,
                (int?)Number(a, "sample_rate"),
                (int?)Number(a, "channels"),
                format.ValueKind == JsonValueKind.Object ? Decimal(format, "duration") : null,
                cover);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("attached_pic", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() == 1;

    static long? Tag(JsonElement stream, string name) =>
        stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object ? Number(tags, name) : null;

    static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ffprobe writes most numbers as strings, and some as numbers.
    static long? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) ? s : null;
    }

    static double? Decimal(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
}
