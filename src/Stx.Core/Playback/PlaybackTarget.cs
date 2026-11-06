using System.Net;
using System.Text.RegularExpressions;
using Stx.Core.Library;

namespace Stx.Core.Playback;

public enum TargetKind { File, Disc, Stream }

/// <summary>
/// What the engine is asked to open, decided before LibVLC sees anything: a file by its path, a disc by an MRL
/// (dvd:/// or bluray:///) once STX has checked it isn't encrypted, or a network stream by its address once STX has
/// checked it doesn't need a listening port. A refusal carries the plate's plain message, and nothing is opened.
/// </summary>
public sealed record PlaybackTarget(TargetKind Kind, string Location, string? Refusal = null, DiscKind Disc = DiscKind.None)
{
    public bool IsRefused => Refusal is not null;

    public static PlaybackTarget For(string pathOrUrl, Func<string, DiscVerdict>? inspect = null)
    {
        // A file: address is the file it names.
        if (pathOrUrl.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(pathOrUrl.Trim(), UriKind.Absolute, out var file) && file.IsFile)
            pathOrUrl = file.LocalPath;

        if (StreamLink.IsUrl(pathOrUrl))
        {
            // Only a stream STX knows reaches the engine: an unknown scheme could open a camera or the desktop
            // (dshow://, screen://) or a share STX never meant to browse (smb://).
            var (kind, why) = StreamLink.Classify(pathOrUrl);
            return kind switch
            {
                LinkKind.Refused => new(TargetKind.Stream, pathOrUrl, why),
                LinkKind.NotALink => new(TargetKind.Stream, pathOrUrl, StreamLink.UnknownRefused),
                _ => new(TargetKind.Stream, StreamLink.ForEngine(pathOrUrl)),
            };
        }

        var verdict = (inspect ?? DiscCheck.Inspect)(pathOrUrl);
        if (verdict.Kind == DiscKind.None) return new(TargetKind.File, pathOrUrl);
        if (verdict.Encrypted) return new(TargetKind.Disc, pathOrUrl, DiscCheck.Refused, verdict.Kind);
        // A single unencrypted VOB plays as the MPEG file it is; everything else goes to the disc reader.
        if (string.Equals(Path.GetExtension(pathOrUrl), ".vob", StringComparison.OrdinalIgnoreCase)) return new(TargetKind.File, pathOrUrl, null, verdict.Kind);
        return new(TargetKind.Disc, DiscMrl(verdict.Kind, DiscRoot(pathOrUrl)), null, verdict.Kind);
    }

    /// <summary>dvd:///D:/ or bluray:///C:/Films/My%20Film/ — the path after the scheme, with forward slashes, each part escaped.</summary>
    public static string DiscMrl(DiscKind kind, string path)
    {
        string scheme = kind == DiscKind.BluRay ? "bluray" : "dvd";
        string forward = path.Replace('\\', '/');
        var parts = forward.Split('/');
        for (int i = 0; i < parts.Length; i++)
            if (!(i == 0 && parts[i].Length == 2 && parts[i][1] == ':')) parts[i] = Uri.EscapeDataString(parts[i]);
        string joined = string.Join('/', parts).TrimStart('/');
        if (Directory.Exists(path) && !joined.EndsWith('/')) joined += "/";
        return $"{scheme}:///{joined}";
    }

    // The disc reader wants the folder that holds VIDEO_TS or BDMV, or the image; a picked VIDEO_TS, BDMV or IFO is walked up from.
    static string DiscRoot(string path)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(path);
        if (File.Exists(trimmed) && Path.GetExtension(trimmed).ToLowerInvariant() is ".ifo" or ".bup") trimmed = Path.GetDirectoryName(trimmed)!;
        return Path.GetFileName(trimmed).ToUpperInvariant() is "VIDEO_TS" or "BDMV" ? Path.GetDirectoryName(trimmed)! : path;
    }
}

public enum LinkKind { NotALink, Stream, Playlist, WebPage, Unknown, Refused }

/// <summary>
/// Sorts a pasted address: a stream LibVLC can play, a radio playlist, a web page (which goes to ANALYZE and the
/// capture path, as before), or one that needs a look at what the server sends. LibVLC only ever connects out:
/// UDP and RTP would need STX to listen on a port, so they're refused (README: no network port).
/// </summary>
public static partial class StreamLink
{
    public const string PortRefused = "STX DOESN'T LISTEN ON NETWORK PORTS, SO UDP AND RTP STREAMS CAN'T BE PLAYED.";
    public const string IsWebPage = "THAT'S A WEB PAGE, NOT A STREAM. PRESS ANALYZE TO SAVE FROM IT.";
    public const string UnknownRefused = "STX PLAYS HTTP, HTTPS, RTSP, MMS, RTMP AND SRT STREAMS. IT DOESN'T KNOW THAT ADDRESS.";

    static readonly string[] Adaptive = [".m3u8", ".mpd"];
    static readonly string[] Playlists = [".pls", ".m3u", ".xspf"];
    static readonly string[] Pages = [".html", ".htm", ".php", ".asp", ".aspx", ".jsp"];

    public static bool IsUrl(string text) => Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && !uri.IsFile && uri.Scheme.Length > 1;

    public static (LinkKind Kind, string? Why) Classify(string text)
    {
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) || uri.IsFile) return (LinkKind.NotALink, null);
        switch (uri.Scheme.ToLowerInvariant())
        {
            case "udp" or "rtp" or "sap" or "udplite":
                return (LinkKind.Refused, PortRefused);
            case "rtsp" or "rtsps" or "mms" or "mmsh" or "mmst" or "rtmp" or "rtmps" or "srt":
                return (LinkKind.Stream, null);
            case "http" or "https" or "icyx":
                string extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (Adaptive.Contains(extension) || MediaTypes.IsMedia(uri.AbsolutePath)) return (LinkKind.Stream, null);
                if (Playlists.Contains(extension)) return (LinkKind.Playlist, null);
                if (Pages.Contains(extension)) return (LinkKind.WebPage, IsWebPage);
                return (LinkKind.Unknown, null);
            default:
                return (LinkKind.NotALink, null);
        }
    }

    /// <summary>What a server's Content-Type says the address is. Null when it doesn't say.</summary>
    public static LinkKind? FromContentType(string? contentType)
    {
        string type = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "audio/x-scpls" or "audio/x-mpegurl" or "audio/mpegurl" or "application/xspf+xml" => LinkKind.Playlist,
            "application/vnd.apple.mpegurl" or "application/x-mpegurl" or "application/dash+xml" or "application/ogg" or "application/octet-stream" => LinkKind.Stream,
            "text/html" or "application/xhtml+xml" => LinkKind.WebPage,
            _ when type.StartsWith("audio/", StringComparison.Ordinal) || type.StartsWith("video/", StringComparison.Ordinal) => LinkKind.Stream,
            _ => null,
        };
    }

    /// <summary>The address as LibVLC is given it: old mms:// is MMS over HTTP, which connects out on one TCP connection.</summary>
    public static string ForEngine(string url)
    {
        string trimmed = url.Trim();
        return trimmed.StartsWith("mms://", StringComparison.OrdinalIgnoreCase) ? "mmsh://" + trimmed[6..] : trimmed;
    }

    /// <summary>The stream addresses in a .pls, .m3u or .xspf radio playlist, in order, made absolute against where it came from.</summary>
    public static IReadOnlyList<string> ReadPlaylist(string text, Uri from)
    {
        var found = new List<string>();
        void Add(string candidate)
        {
            candidate = WebUtility.HtmlDecode(candidate.Trim());
            if (candidate.Length == 0 || candidate.StartsWith('#')) return;
            if (Uri.TryCreate(from, candidate, out var uri) && uri.Scheme is "http" or "https" or "rtsp" or "mms" or "mmsh" && !found.Contains(uri.AbsoluteUri))
                found.Add(uri.AbsoluteUri);
        }

        if (text.Contains("[playlist]", StringComparison.OrdinalIgnoreCase))
            foreach (Match m in PlsEntry().Matches(text)) Add(m.Groups[1].Value);
        else if (text.Contains("<location>", StringComparison.OrdinalIgnoreCase))
            foreach (Match m in XspfEntry().Matches(text)) Add(m.Groups[1].Value);
        else
            foreach (string line in text.Split('\n')) Add(line);
        return found;
    }

    [GeneratedRegex(@"^\s*File\d+\s*=\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PlsEntry();

    [GeneratedRegex(@"<location>\s*(.+?)\s*</location>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex XspfEntry();
}
