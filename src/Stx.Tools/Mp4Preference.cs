using System.Globalization;

namespace Stx.Tools;

/// <summary>
/// yt-dlp picks MKV whenever a page doesn't declare a stream's codec, even when the streams fit MP4. This looks at the
/// saved file and moves it into MP4 without re-encoding. Any doubt or failure leaves the MKV exactly as it was, because
/// a container preference is never worth losing a finished download.
/// </summary>
public sealed class Mp4Preference(IProcessRunner process)
{
    // The same codecs yt-dlp itself accepts for MP4 when it knows them.
    static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase) { "h264", "hevc", "av1" };
    static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { "aac" };

    public async Task<string> PreferAsync(string path, string? ffmpeg, CancellationToken cancel = default)
    {
        if (ffmpeg is null || !path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) return path;
        string target = Path.ChangeExtension(path, ".mp4");
        if (File.Exists(target)) return await SameStreams(ffmpeg, path, target, cancel).ConfigureAwait(false) ? KeepEarlier(target, path) : path;

        string partial = Path.Combine(Path.GetDirectoryName(path) ?? "", $"stx.partial-{Guid.NewGuid():N}.mp4");
        try
        {
            string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", "ffprobe.exe");
            if (!await FitsMp4(ffprobe, path, cancel).ConfigureAwait(false)) return path;

            string[] args = ["-v", "error", "-i", path, "-map", "0", "-c", "copy", "-dn", "-movflags", "+faststart", partial];
            int exit = await process.RunAsync(ffmpeg, args, _ => { }, cancel).ConfigureAwait(false);
            if (exit != 0 || !File.Exists(partial)) return path;

            File.Move(partial, target);
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Something holds the MKV open. Two copies of one video is worse than the MKV alone.
                File.Delete(target);
                return path;
            }
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return path;
        }
        // A cancel isn't swallowed (review 2026-11-07): it used to come back as the MKV, and the job reported success.
        finally
        {
            TryDelete(partial);
        }
    }

    // Downloading a video again after its MKV moved to MP4 gives an MKV beside the MP4. Their bytes differ, so only
    // the streams inside can show it's the same video. A different video that merely shares the name keeps both files.
    async Task<bool> SameStreams(string ffmpeg, string first, string second, CancellationToken cancel)
    {
        try
        {
            var one = await StreamHashes(ffmpeg, first, cancel).ConfigureAwait(false);
            var two = await StreamHashes(ffmpeg, second, cancel).ConfigureAwait(false);
            return one is { Count: > 0 } && two is not null && one.SequenceEqual(two);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    async Task<List<string>?> StreamHashes(string ffmpeg, string path, CancellationToken cancel)
    {
        var hashes = new List<string>();
        string[] args = ["-v", "error", "-i", path, "-map", "0:v", "-map", "0:a?", "-c", "copy", "-f", "streamhash", "-hash", "sha256", "-"];
        int exit = await process.RunAsync(ffmpeg, args, line => { if (line.Contains("SHA256=", StringComparison.Ordinal)) lock (hashes) hashes.Add(line.Trim()); }, cancel).ConfigureAwait(false);
        if (exit != 0) return null;
        lock (hashes) return [.. hashes];
    }

    static string KeepEarlier(string earlier, string copy)
    {
        try
        {
            File.Delete(copy);
            return earlier;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return copy;
        }
    }

    async Task<bool> FitsMp4(string ffprobe, string path, CancellationToken cancel)
    {
        var lines = new List<string>();
        string[] args = ["-v", "error", "-show_entries", "stream=codec_name,codec_type", "-of", "csv=p=0", path];
        int exit = await process.RunAsync(ffprobe, args, line => { lock (lines) lines.Add(line); }, cancel).ConfigureAwait(false);
        if (exit != 0) return false;

        bool anyVideo = false;
        lock (lines)
        {
            foreach (string line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                string[] parts = line.Trim().Split(',');
                string codec = parts[0], kind = parts.Length > 1 ? parts[1] : "";
                switch (kind.ToLower(CultureInfo.InvariantCulture))
                {
                    case "video" when Video.Contains(codec): anyVideo = true; break;
                    case "audio" when Audio.Contains(codec): break;
                    // Timed data from a stream isn't picture or sound, and -dn drops it. Subtitles and attachments are kept by staying MKV.
                    case "data": break;
                    default: return false;
                }
            }
        }
        return anyVideo;
    }

    static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A stray temporary file is harmless, and its name says what it is.
        }
    }
}
