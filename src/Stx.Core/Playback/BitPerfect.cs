using System.Globalization;
using System.Text.Json;

namespace Stx.Core.Playback;

/// <summary>A file's sound as ffprobe reports it, for choosing how it reaches the device.</summary>
public sealed record PcmSource(string Codec, int Bits, int Rate, int Channels, bool HasVideo, double? Seconds)
{
    /// <summary>Codecs whose decoded samples are the exact samples that were encoded: any correct decoder gives the same bits.</summary>
    static readonly HashSet<string> Lossless = new(StringComparer.OrdinalIgnoreCase)
    {
        "flac", "alac", "wavpack", "ape", "tta", "mlp", "truehd", "shorten", "tak",
        "pcm_s16le", "pcm_s16be", "pcm_s24le", "pcm_s24be", "pcm_s32le", "pcm_s32be", "pcm_u8", "pcm_s8",
    };

    public bool IsLossless => Lossless.Contains(Codec);

    /// <summary>The first audio stream of ffprobe's JSON (-show_streams -show_format), or null when there's none.</summary>
    public static PcmSource? FromProbe(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) return null;
            JsonElement? audio = null;
            bool video = false;
            foreach (var stream in streams.EnumerateArray())
            {
                string? type = Text(stream, "codec_type");
                if (type == "audio") audio ??= stream;
                else if (type == "video" && !(stream.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var p) && p.ValueKind == JsonValueKind.Number && p.GetInt32() == 1))
                    video = true;
            }
            if (audio is not { } a) return null;
            int bits = Int(a, "bits_per_raw_sample") is > 0 and var raw ? raw
                : Int(a, "bits_per_sample") is > 0 and var sample ? sample
                : (Text(a, "sample_fmt") ?? "") switch { "s16" or "s16p" => 16, "s32" or "s32p" => 32, "u8" or "u8p" => 8, _ => 0 };
            double? seconds = document.RootElement.TryGetProperty("format", out var format) && Text(format, "duration") is { } d2
                && double.TryParse(d2, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : null;
            return new PcmSource(Text(a, "codec_name") ?? "", bits, Int(a, "sample_rate") ?? 0, Int(a, "channels") ?? 0, video, seconds);
        }
        catch (JsonException) { return null; }
    }

    static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) => n,
            _ => null,
        } : null;
}

/// <summary>How a file's sound reaches the device.</summary>
public enum AudioRoute
{
    /// <summary>Through VLC's engine, shared with Windows. BIT-PERFECT is off, or the file can't be bit-perfect.</summary>
    Shared,
    /// <summary>Music: FFmpeg decodes, STX's renderer holds the device exclusively at the file's own format.</summary>
    Exclusive,
    /// <summary>A film's AC3, E-AC3, DTS or TrueHD soundtrack, sent to the receiver untouched through VLC's exclusive passthrough.</summary>
    Passthrough,
}

/// <summary>A PCM layout a device may be opened with: the container's bits, the bits that carry sound, and FFmpeg's matching output.</summary>
public sealed record DeviceFormat(int Container, int Valid, string FfmpegCodec, string FfmpegFormat);

/// <summary>
/// BIT-PERFECT (Phase 5). Spike S5 read VLC 3.0.24's source: its Windows output opens plain PCM shared only
/// (modules/audio_output/wasapi.c:546-548), and its audio callbacks take 16-bit samples only
/// (modules/audio_output/amem.c:191-193). So, with Rocky's choice: music from a lossless file is decoded by FFmpeg and
/// played by STX's own renderer, which holds the device exclusively at the file's own rate and depth; a film's
/// compressed soundtrack goes to the receiver through VLC's exclusive passthrough; anything else plays shared and the
/// readout says why. Nothing is resampled, mixed, dithered or turned up or down on the bit-perfect routes.
/// </summary>
public static class BitPerfect
{
    public const string ExclusiveWarning = "WHILE BIT-PERFECT IS ON, OTHER APPS CAN'T MAKE SOUND.";

    static readonly HashSet<string> Bitstreams = new(StringComparer.OrdinalIgnoreCase) { "ac3", "eac3", "dts", "truehd", "mlp" };

    public static AudioRoute Route(bool bitPerfect, PcmSource? source)
    {
        if (!bitPerfect || source is null) return AudioRoute.Shared;
        if (source.HasVideo) return Bitstreams.Contains(source.Codec) ? AudioRoute.Passthrough : AudioRoute.Shared;
        return source.IsLossless && source.Bits is >= 8 and <= 32 && source.Rate > 0 && source.Channels is > 0 and <= 8 ? AudioRoute.Exclusive : AudioRoute.Shared;
    }

    /// <summary>
    /// The device layouts that carry the file's samples unaltered, best first. A wider container only pads with zeros,
    /// which changes no sample; a narrower one would cut bits, so it's never offered. The rate is always the file's.
    /// </summary>
    public static IReadOnlyList<DeviceFormat> Candidates(int bits) => bits switch
    {
        <= 16 => [new(16, 16, "pcm_s16le", "s16le"), new(24, 24, "pcm_s24le", "s24le"), new(32, 24, "pcm_s32le", "s32le"), new(32, 32, "pcm_s32le", "s32le")],
        <= 24 => [new(32, 24, "pcm_s32le", "s32le"), new(24, 24, "pcm_s24le", "s24le"), new(32, 32, "pcm_s32le", "s32le")],
        _ => [new(32, 32, "pcm_s32le", "s32le")],
    };

    /// <summary>FFmpeg's command for the file's first audio stream as raw PCM on stdout: no resampling, no channel change, no dither.</summary>
    public static IReadOnlyList<string> FfmpegArgs(string path, DeviceFormat format, TimeSpan from)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        if (from > TimeSpan.Zero) args.AddRange(["-ss", from.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)]);
        args.AddRange(["-i", path, "-map", "0:a:0", "-vn", "-sn", "-dn", "-c:a", format.FfmpegCodec, "-f", format.FfmpegFormat, "-"]);
        return args;
    }

    /// <summary>What the readout says, from what was actually opened on the device.</summary>
    public static string Readout(AudioRoute route, int bits, int rate, string? why = null)
    {
        string khz = (rate / 1000.0).ToString(rate % 1000 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return route switch
        {
            AudioRoute.Exclusive => $"{bits}/{khz} BIT-PERFECT",
            AudioRoute.Passthrough => "BITSTREAM TO RECEIVER",
            _ => why ?? "SHARED WITH WINDOWS",
        };
    }

    /// <summary>Why a file plays shared while BIT-PERFECT is on, in the readout's words.</summary>
    public static string WhyShared(PcmSource? source) => source switch
    {
        null => "SHARED: NO SOUND FOUND TO CHECK",
        { HasVideo: true } => "SHARED: VLC'S ENGINE CAN'T HAND A FILM'S PCM OVER UNALTERED",
        { IsLossless: false } => "SHARED: A LOSSY FILE HAS NO ORIGINAL SAMPLES",
        _ => "SHARED: THIS LAYOUT CAN'T BE SENT AS IT IS",
    };
}
