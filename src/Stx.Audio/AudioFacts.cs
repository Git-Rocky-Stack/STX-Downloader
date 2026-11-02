using System.Globalization;
using System.Text.Json;

namespace Stx.Audio;

/// <summary>What's known about an item's audio. Label says where it came from: measured from the file, or only reported by the site.</summary>
public sealed record AudioFacts(
    string Codec,
    int? BitrateKbps,
    int? SampleRate,
    int? Channels,
    double? RollOffHz,
    double? LoudnessLufs,
    double? TruePeakDb,
    double? PeakLeftDb,
    double? PeakRightDb,
    IReadOnlyList<SpectrumBand> Spectrum,
    IReadOnlyList<EnvelopePoint> Envelope,
    bool MeasuredFromFile)
{
    public const string NoAudioMessage = "No audio stream";

    static readonly string[] LosslessCodecs = ["flac", "alac", "wavpack", "tta", "ape", "truehd", "mlp", "shorten"];

    public string Label => MeasuredFromFile ? "Measured" : "Reported";

    public bool IsLossy => IsLossyCodec(Codec);

    /// <summary>Lossless is only ever claimed for a codec known to be lossless.</summary>
    public static bool IsLossyCodec(string codec) =>
        !(LosslessCodecs.Contains(codec) || codec.StartsWith("pcm_", StringComparison.Ordinal));

    public static AudioFacts Measured(ProbeFacts probe, SpectrumResult spectrum, Loudness loudness) => new(
        probe.Codec, probe.BitrateKbps, probe.SampleRate, probe.Channels,
        spectrum.RollOffHz, loudness.Lufs, loudness.TruePeakDb, spectrum.PeakLeftDb, spectrum.PeakRightDb,
        spectrum.Spectrum, spectrum.Envelope, MeasuredFromFile: true);
}

/// <summary>Before a download, all there is to go on is what the site says about itself.</summary>
public static class ReportedFacts
{
    public static AudioFacts From(YtDlpFormat format) => new(
        format.Codec, format.Bitrate, format.SampleRate, format.Channels,
        null, null, null, null, null, [], [], MeasuredFromFile: false);
}

public sealed record YtDlpFormat(string Codec, int? Bitrate, int? SampleRate, int? Channels)
{
    /// <summary>The audio formats in yt-dlp's JSON. Video-only formats are left out.</summary>
    public static IReadOnlyList<YtDlpFormat> ParseAll(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<YtDlpFormat>();
            foreach (var item in formats.EnumerateArray())
            {
                string? codec = item.TryGetProperty("acodec", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (codec is null or "none") continue;
                list.Add(new YtDlpFormat(codec, Whole(item, "abr"), Whole(item, "asr"), Whole(item, "audio_channels")));
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static YtDlpFormat? Best(IEnumerable<YtDlpFormat> formats) => formats.MaxBy(f => f.Bitrate ?? -1);

    static int? Whole(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)Math.Round(v.GetDouble(), MidpointRounding.AwayFromZero) : null;
}
