using System.Net.Http;
using Stx.Core.Playback;

namespace Stx.ViewModels;

/// <summary>What to play for a pasted address, or why not.</summary>
public sealed record StreamChoice(string? Url, string Title, IReadOnlyList<string> Fallbacks, string Message)
{
    public bool CanPlay => Url is not null;
}

/// <summary>
/// Turns a pasted address into something the PLAYER plate can play: a stream as it is, the first stream of a radio
/// playlist (the rest kept for NEXT), or a plain answer: a web page goes to ANALYZE, a UDP or RTP address is refused.
/// When the address doesn't say what it is, STX asks the server for its headers only; nothing is downloaded.
/// </summary>
public sealed class StreamOpener(HttpClient http)
{
    const int MostPlaylistBytes = 64 * 1024;
    public const string Unreachable = "THAT ADDRESS COULDN'T BE REACHED. CHECK IT AND YOUR CONNECTION.";
    public const string NoStreams = "THAT PLAYLIST HAS NO STREAMS STX CAN PLAY.";

    public async Task<StreamChoice> ResolveAsync(string text, CancellationToken cancel = default)
    {
        string url = text.Trim();
        var (kind, why) = StreamLink.Classify(url);
        if (kind == LinkKind.NotALink) return new(null, "", [], "PASTE A STREAM ADDRESS FIRST.");
        if (kind is LinkKind.Refused or LinkKind.WebPage) return new(null, "", [], why!);
        if (kind == LinkKind.Unknown)
        {
            (kind, string? failure) = await ProbeAsync(url, cancel).ConfigureAwait(false);
            if (failure is not null) return new(null, "", [], failure);
            if (kind == LinkKind.WebPage) return new(null, "", [], StreamLink.IsWebPage);
        }
        if (kind == LinkKind.Playlist) return await FromPlaylistAsync(url, cancel).ConfigureAwait(false);
        return new(url, TitleOf(url), [], "");
    }

    async Task<(LinkKind Kind, string? Failure)> ProbeAsync(string url, CancellationToken cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Icy-MetaData", "0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (LinkKind.Unknown, Unreachable);
            return (StreamLink.FromContentType(response.Content.Headers.ContentType?.ToString()) ?? LinkKind.Stream, null);
        }
        catch (HttpRequestException e) when (e.HttpRequestError == HttpRequestError.InvalidResponse)
        {
            // Old Shoutcast servers answer "ICY 200 OK", which isn't HTTP. LibVLC understands them; .NET doesn't.
            return (LinkKind.Stream, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return (LinkKind.Unknown, Unreachable);
        }
    }

    async Task<StreamChoice> FromPlaylistAsync(string url, CancellationToken cancel)
    {
        string text;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(null, "", [], Unreachable);
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            var buffer = new byte[MostPlaylistBytes];
            int read = 0, n;
            while (read < buffer.Length && (n = await body.ReadAsync(buffer.AsMemory(read), cancel).ConfigureAwait(false)) > 0) read += n;
            text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return new(null, "", [], Unreachable);
        }
        var streams = StreamLink.ReadPlaylist(text, new Uri(url)).Where(s => StreamLink.Classify(s).Kind is not (LinkKind.Refused or LinkKind.WebPage)).ToList();
        if (streams.Count == 0) return new(null, "", [], NoStreams);
        return new(streams[0], TitleOf(url), streams.Skip(1).ToList(), "");
    }

    /// <summary>The plate's title for a stream: its host, and the file name when there is one.</summary>
    public static string TitleOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        string name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.AbsolutePath));
        return name.Length > 0 ? $"{uri.Host} · {name}" : uri.Host;
    }
}
