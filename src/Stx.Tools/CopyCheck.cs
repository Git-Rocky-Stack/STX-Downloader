using System.Globalization;
using System.Text;

namespace Stx.Tools;

/// <summary>
/// Checks a new copy before it's marked done (Phase 7): it decodes from start to end with no errors, it has the
/// original's picture and sound streams, and it lasts as long as the original. A copy that fails is never kept.
/// </summary>
public sealed class CopyCheck(IProcessRunner process, string ffmpegPath, string ffprobePath)
{
    /// <summary>How far the lengths may differ: half a second, or 1% of a long film.</summary>
    public static TimeSpan Tolerance(TimeSpan length) => TimeSpan.FromSeconds(Math.Max(0.5, length.TotalSeconds * 0.01));

    /// <summary>Null when the copy is sound; otherwise what's wrong with it, in plain words for the job's log.</summary>
    public async Task<string?> ProblemAsync(string original, string copy, CancellationToken cancel = default)
    {
        var (originalLength, originalStreams) = await ProbeAsync(original, cancel).ConfigureAwait(false);
        var (copyLength, copyStreams) = await ProbeAsync(copy, cancel).ConfigureAwait(false);
        if (copyLength is null) return "the copy's length couldn't be read";
        if (originalLength is { } want && Math.Abs((copyLength.Value - want).TotalSeconds) > Tolerance(want).TotalSeconds)
            return $"the copy lasts {copyLength.Value.TotalSeconds:0.00} s and the original {want.TotalSeconds:0.00} s";
        if (originalStreams.Video > 0 && copyStreams.Video == 0) return "the copy has no picture";
        if (originalStreams.Audio > 0 && copyStreams.Audio == 0) return "the copy has no sound";

        var errors = new StringBuilder();
        int exit = await process.RunAsync(ffmpegPath, ["-hide_banner", "-nostdin", "-v", "error", "-i", copy, "-map", "0:v?", "-map", "0:a?", "-f", "null", "-"],
            line => { if (!string.IsNullOrWhiteSpace(line)) lock (errors) errors.AppendLine(line); }, cancel).ConfigureAwait(false);
        string decodeErrors;
        lock (errors) decodeErrors = errors.ToString().Trim();
        if (exit != 0 || decodeErrors.Length > 0) return "the copy didn't decode cleanly: " + (decodeErrors.Length > 0 ? decodeErrors.Split('\n')[0] : $"exit code {exit}");
        return null;
    }

    async Task<(TimeSpan? Length, (int Video, int Audio) Streams)> ProbeAsync(string path, CancellationToken cancel)
    {
        var lines = new List<string>();
        await process.RunAsync(ffprobePath, ["-v", "error", "-show_entries", "format=duration:stream=codec_type:stream_disposition=attached_pic", "-of", "compact=p=0:nk=1", path],
            line => { lock (lines) lines.Add(line); }, cancel).ConfigureAwait(false);
        TimeSpan? length = null;
        int video = 0, audio = 0;
        lock (lines)
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split('|');
                if (parts[0] == "video" && !(parts.Length > 1 && parts[1] == "1")) video++;
                else if (parts[0] == "audio") audio++;
                else if (parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) length = TimeSpan.FromSeconds(seconds);
            }
        return (length, (video, audio));
    }
}
