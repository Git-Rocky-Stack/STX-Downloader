namespace Stx.Core.Playback;

/// <summary>
/// Whether a file carries Dirac / VC-2 video, read from its bytes rather than from LibVLC. VLC 3.0.24's engine crashes
/// the process on Dirac (the official desktop build too), so when LibVLC can't finish reading a file's tracks, STX
/// looks for Dirac itself instead of assuming the file is safe.
/// </summary>
/// <remarks>
/// Every Dirac stream opens with a sequence header: the parse-info prefix "BBCD" followed by parse code 0x00. FFmpeg
/// puts it within the first few kilobytes in MKV, MOV, MP4, MPEG-TS, NUT and AVI (checked 2026-11-07); Matroska also
/// names the codec "V_DIRAC". A five-byte signature in a megabyte of other data is a one-in-a-million coincidence.
/// </remarks>
public static class DiracSniff
{
    /// <summary>What the PLAYER plate says when it refuses Dirac.</summary>
    public const string Refused = "DIRAC VIDEO CAN'T BE PLAYED SAFELY";

    /// <summary>How much of a stream is waited for before it plays anyway: a slow server isn't kept from playing.</summary>
    public static readonly TimeSpan StreamPatience = TimeSpan.FromSeconds(2);

    const int Window = 1024 * 1024;
    static ReadOnlySpan<byte> SequenceHeader => "BBCD\0"u8;
    static ReadOnlySpan<byte> MatroskaCodec => "V_DIRAC"u8;

    /// <summary>
    /// Whether an http(s) stream carries Dirac, read from its first bytes before LibVLC is given it (review 2026-11-07:
    /// LibVLC's own check reads local files only). Most streams are never read: one whose address ends in a sound file's
    /// extension, or whose server says it's sound, a playlist, a manifest (HLS, DASH) or a page, and an old Shoutcast
    /// server (which answers "ICY", not HTTP). Anything else is read up to a megabyte, for at most
    /// <see cref="StreamPatience"/> (or <paramref name="patience"/>); what arrived in that time is checked, and a stream
    /// that can't be reached is false, since LibVLC will say so itself. Never throws.
    /// </summary>
    public static async Task<bool> InStreamAsync(HttpClient http, string url, TimeSpan? patience = null, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        if (Library.MediaTypes.KindOf(uri.AbsolutePath) == Library.MediaKind.Audio) return false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(patience ?? StreamPatience);
        var buffer = new byte[Window];
        int read = 0;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Icy-MetaData", "0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || !MayCarryVideo(response.Content.Headers.ContentType?.MediaType)) return false;
            await using var body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            int n;
            while (read < buffer.Length && (n = await body.ReadAsync(buffer.AsMemory(read), limit.Token).ConfigureAwait(false)) > 0) read += n;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            // Out of patience: what arrived is checked.
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException)
        {
            return false;
        }
        return Present(buffer.AsSpan(0, read));
    }

    // A video stream, or one whose server doesn't say: "application/octet-stream" and no type at all included.
    static bool MayCarryVideo(string? mediaType)
    {
        string type = (mediaType ?? "").ToLowerInvariant();
        if (type.StartsWith("audio/", StringComparison.Ordinal) || type.StartsWith("text/", StringComparison.Ordinal)) return false;
        return type is not ("application/vnd.apple.mpegurl" or "application/x-mpegurl" or "application/dash+xml" or "application/xspf+xml"
            or "application/xhtml+xml" or "application/json" or "application/xml");
    }

    /// <summary>True when the file's first megabyte holds Dirac's sequence header or Matroska's Dirac codec name.
    /// A file that can't be read is false: LibVLC won't get further with it than STX did.</summary>
    public static bool Present(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var buffer = new byte[Window];
            int read = 0, n;
            while (read < buffer.Length && (n = file.Read(buffer, read, buffer.Length - read)) > 0) read += n;
            return Present(buffer.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The same check on bytes already read.</summary>
    public static bool Present(ReadOnlySpan<byte> head) => head.IndexOf(SequenceHeader) >= 0 || head.IndexOf(MatroskaCodec) >= 0;
}
