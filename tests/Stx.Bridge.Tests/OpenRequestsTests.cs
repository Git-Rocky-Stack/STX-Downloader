using System.Collections.Concurrent;
using System.IO.Pipes;
using Stx.Bridge;
using Xunit;

namespace Stx.Bridge.Tests;

/// <summary>A second launch hands its file to the running STX over a per-user named pipe.</summary>
public class OpenRequestsTests
{
    static string PipeName() => "stx-open-test-" + Guid.NewGuid().ToString("N");

    static bool Within(TimeSpan limit, Func<bool> condition)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until) { if (condition()) return true; Thread.Sleep(20); }
        return condition();
    }

    [Fact]
    public async Task A_sent_path_arrives_once_unchanged()
    {
        string name = PipeName();
        var got = new ConcurrentQueue<string>();
        using var server = new OpenRequestServer(name, got.Enqueue);
        const string path = @"D:\Фильмы\My Movie (2019).mkv";
        Assert.True(await OpenRequests.SendAsync(name, path, TimeSpan.FromSeconds(5)));
        Assert.True(Within(TimeSpan.FromSeconds(5), () => !got.IsEmpty));
        Thread.Sleep(200);
        Assert.Equal([path], got);
    }

    [Fact]
    public async Task Send_gives_up_within_its_timeout_when_nothing_listens()
    {
        var started = DateTime.UtcNow;
        Assert.False(await OpenRequests.SendAsync(PipeName(), @"D:\a.mkv", TimeSpan.FromMilliseconds(500)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task An_oversized_message_is_refused_and_the_server_keeps_listening()
    {
        string name = PipeName();
        var got = new ConcurrentQueue<string>();
        using var server = new OpenRequestServer(name, got.Enqueue);
        using (var client = new NamedPipeClientStream(".", name, PipeDirection.Out))
        {
            await client.ConnectAsync(5000);
            try
            {
                await client.WriteAsync(BitConverter.GetBytes(64 * 1024));
                await client.WriteAsync(new byte[64 * 1024]);
            }
            catch (IOException)
            {
                // The server refused on the length and closed the pipe, so the rest of this write breaks. That's the refusal.
            }
        }
        Assert.True(await OpenRequests.SendAsync(name, @"D:\after.mkv", TimeSpan.FromSeconds(5)));
        Assert.True(Within(TimeSpan.FromSeconds(5), () => got.Contains(@"D:\after.mkv")));
        Assert.Equal([@"D:\after.mkv"], got);
    }

    // Review 2026-11-07: an exception from handing the path on (anything but four pipe errors) ended the listener, and
    // every later "Open with" was lost until STX restarted.
    [Fact]
    public async Task A_path_that_fails_to_hand_on_doesnt_stop_the_next()
    {
        string name = PipeName();
        var got = new ConcurrentQueue<string>();
        using var server = new OpenRequestServer(name, path =>
        {
            if (path.EndsWith("bad.mkv", StringComparison.Ordinal)) throw new ArgumentException("the window was closing");
            got.Enqueue(path);
        });
        Assert.True(await OpenRequests.SendAsync(name, @"D:\bad.mkv", TimeSpan.FromSeconds(5)));
        Thread.Sleep(300);
        Assert.True(await OpenRequests.SendAsync(name, @"D:\good.mkv", TimeSpan.FromSeconds(5)));
        Assert.True(Within(TimeSpan.FromSeconds(5), () => got.Contains(@"D:\good.mkv")), "the listener stopped");
    }
}
