using System.Net;
using System.Net.Http.Headers;
using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>
/// Review 2026-11-07, "streams aren't checked for Dirac": LibVLC's check reads local files only, so a Dirac stream
/// reached the decoder that crashes VLC 3.0.24's engine (it still does, 2026-11-07: with each Dirac plugin taken out,
/// with one decoding thread and with hardware decoding off). The first bytes of an http(s) stream are read for it.
/// </summary>
public class DiracStreamTests
{
    static byte[] Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "tests", "fixtures", "playback", name));
    }

    /// <summary>A server: answers with a type and a body, and says whether the body was read and how often it was asked.</summary>
    sealed class Server(string? type, byte[] body, HttpStatusCode status = HttpStatusCode.OK, TimeSpan? trickle = null) : HttpMessageHandler
    {
        public int Asked;
        public Watched? Body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Interlocked.Increment(ref Asked);
            Body = new Watched(body, trickle);
            var content = new StreamContent(Body);
            if (type is not null) content.Headers.ContentType = new MediaTypeHeaderValue(type);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    /// <summary>A body that notes being read, and can arrive slowly, like a live stream.</summary>
    sealed class Watched(byte[] bytes, TimeSpan? trickle) : MemoryStream(bytes)
    {
        public bool WasRead;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            WasRead = true;
            if (trickle is { } wait) await Task.Delay(wait, cancel);
            return await base.ReadAsync(trickle is null ? buffer : buffer[..Math.Min(buffer.Length, 4096)], cancel);
        }
    }

    static Task<bool> Check(Server server, string url = "https://example.com/live", TimeSpan? patience = null) =>
        DiracSniff.InStreamAsync(new HttpClient(server), url, patience, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("video/x-matroska")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public async Task A_dirac_stream_is_found_by_its_first_bytes(string? type) =>
        Assert.True(await Check(new Server(type, Fixture("tiny-dirac.mkv"))));

    [Fact]
    public async Task Other_video_is_not_dirac() => Assert.False(await Check(new Server("video/mp4", Fixture("tiny.mp4"))));

    // Radio, playlists, manifests and pages are never read: sound can't hold Dirac, and LibVLC reads the rest itself.
    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/aacp")]
    [InlineData("application/vnd.apple.mpegurl")]
    [InlineData("application/dash+xml")]
    [InlineData("text/html")]
    public async Task Sound_playlists_and_pages_are_never_read(string type)
    {
        var server = new Server(type, Fixture("tiny-dirac.mkv"));
        Assert.False(await Check(server));
        Assert.False(server.Body!.WasRead);
    }

    // A radio address that names its sound file isn't even asked: no second connection before radio plays.
    [Theory]
    [InlineData("https://radio.example.com/live.mp3")]
    [InlineData("http://radio.example.com/stream.aac")]
    [InlineData("rtsp://camera.example.com/feed")]
    [InlineData("not an address")]
    public async Task Sound_by_its_address_and_other_protocols_are_not_asked(string url)
    {
        var server = new Server("video/x-matroska", Fixture("tiny-dirac.mkv"));
        Assert.False(await Check(server, url));
        Assert.Equal(0, server.Asked);
    }

    // A slow server is waited for only so long, and what arrived in that time is checked.
    [Fact]
    public async Task A_slow_stream_is_checked_with_what_arrived_in_time()
    {
        // The Dirac file, then a megabyte more: at 4 KB every 40 ms, only running out of patience ends the read.
        byte[] dirac = [.. Fixture("tiny-dirac.mkv"), .. new byte[1024 * 1024]];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(await Check(new Server("video/x-matroska", dirac, trickle: TimeSpan.FromMilliseconds(40)), patience: TimeSpan.FromMilliseconds(400)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"waited {clock.Elapsed}");
    }

    [Fact]
    public async Task A_stream_that_cant_be_reached_is_left_to_libvlc()
    {
        Assert.False(await Check(new Server("video/x-matroska", Fixture("tiny-dirac.mkv"), HttpStatusCode.NotFound)));
        Assert.False(await DiracSniff.InStreamAsync(new HttpClient(new Refusing()), "https://example.com/live", null, TestContext.Current.CancellationToken));
    }

    sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            throw new HttpRequestException("refused", null, HttpStatusCode.ServiceUnavailable);
    }
}
