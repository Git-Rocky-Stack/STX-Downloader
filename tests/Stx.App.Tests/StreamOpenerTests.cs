using System.Net;
using System.Net.Http;
using Stx.Core.Playback;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>A server that answers from a table, and remembers what was asked.</summary>
sealed class TableHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public readonly List<string> Asked = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Asked.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(answer(request));
    }
}

/// <summary>Phase 4: network streams and internet radio, pasted on the Source plate and played on the PLAYER plate.</summary>
public class StreamOpenerTests
{
    static HttpResponseMessage With(string contentType, string body = "")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    static (StreamOpener, TableHandler) Make(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new TableHandler(answer);
        return (new StreamOpener(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task A_stream_address_plays_as_it_is_without_asking_the_server()
    {
        var (opener, handler) = Make(_ => throw new InvalidOperationException("no request needed"));
        var choice = await opener.ResolveAsync("  https://cdn.example.com/live/master.m3u8 ");
        Assert.Equal("https://cdn.example.com/live/master.m3u8", choice.Url);
        Assert.Equal("cdn.example.com · master", choice.Title);
        Assert.Empty(handler.Asked);
    }

    [Fact]
    public async Task An_address_that_doesnt_say_is_asked_for_its_headers()
    {
        var (opener, _) = Make(_ => With("audio/mpeg"));
        var choice = await opener.ResolveAsync("http://ice.example.com:8000/live");
        Assert.True(choice.CanPlay);
        Assert.Equal("http://ice.example.com:8000/live", choice.Url);
    }

    [Fact]
    public async Task A_web_page_goes_to_analyze()
    {
        var (opener, _) = Make(_ => With("text/html", "<html></html>"));
        var choice = await opener.ResolveAsync("https://www.example.com/watch/12345");
        Assert.False(choice.CanPlay);
        Assert.Equal(StreamLink.IsWebPage, choice.Message);
    }

    [Fact]
    public async Task A_radio_playlist_plays_its_first_stream_and_keeps_the_rest()
    {
        var (opener, _) = Make(_ => With("audio/x-scpls", "[playlist]\nFile1=http://ice1.example.com/jazz\nFile2=http://ice2.example.com/jazz\n"));
        var choice = await opener.ResolveAsync("https://radio.example.com/listen.pls");
        Assert.Equal("http://ice1.example.com/jazz", choice.Url);
        Assert.Equal(["http://ice2.example.com/jazz"], choice.Fallbacks);
    }

    [Fact]
    public async Task A_playlist_found_by_its_content_type_is_read_too()
    {
        var (opener, handler) = Make(r => With("audio/x-mpegurl", "#EXTM3U\nhttp://ice.example.com/a.mp3\n"));
        var choice = await opener.ResolveAsync("http://radio.example.com/tune-in");
        Assert.Equal("http://ice.example.com/a.mp3", choice.Url);
        Assert.Equal(2, handler.Asked.Count);
    }

    [Fact]
    public async Task A_playlist_of_only_port_streams_plays_nothing()
    {
        var (opener, _) = Make(_ => With("audio/x-mpegurl", "udp://@239.0.0.1:1234\n"));
        var choice = await opener.ResolveAsync("https://radio.example.com/listen.m3u");
        Assert.False(choice.CanPlay);
        Assert.Equal(StreamOpener.NoStreams, choice.Message);
    }

    [Fact]
    public async Task Udp_is_refused_plainly()
    {
        var (opener, handler) = Make(_ => throw new InvalidOperationException());
        var choice = await opener.ResolveAsync("udp://@239.0.0.1:1234");
        Assert.Equal(StreamLink.PortRefused, choice.Message);
        Assert.Empty(handler.Asked);
    }

    [Fact]
    public async Task An_unreachable_server_says_so()
    {
        var (opener, _) = Make(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError));
        Assert.Equal(StreamOpener.Unreachable, (await opener.ResolveAsync("http://nowhere.example.com/live")).Message);
    }

    // Old Shoutcast servers answer "ICY 200 OK". .NET can't read that; LibVLC can.
    [Fact]
    public async Task An_icy_server_is_left_to_libvlc()
    {
        var (opener, _) = Make(_ => throw new HttpRequestException(HttpRequestError.InvalidResponse));
        Assert.True((await opener.ResolveAsync("http://old.example.com:8000/")).CanPlay);
    }

    [Fact]
    public async Task Nothing_pasted_asks_for_an_address()
    {
        var (opener, _) = Make(_ => throw new InvalidOperationException());
        Assert.False((await opener.ResolveAsync("")).CanPlay);
    }
}
