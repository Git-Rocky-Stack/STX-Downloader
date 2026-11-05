using System.IO.Pipes;
using System.Text;

namespace Stx.Bridge;

/// <summary>
/// A second launch with a file hands the file to the STX already running, over a named pipe only your account can
/// open (the same rules as the browser hand-off, PipeServer.CreatePipe). No network port is opened.
/// Each message is a 4-byte length and the path in UTF-8.
/// </summary>
public static class OpenRequests
{
    /// <summary>The longest path accepted, in bytes. Windows paths stop at 32,767 characters; anything near this is not a path.</summary>
    public const int MostBytes = 32 * 1024;

    public static string NameForCurrentUser() => $"stx-open-{System.Security.Principal.WindowsIdentity.GetCurrent().User}";

    /// <summary>Sends the path; false if no STX is listening within the timeout.</summary>
    public static async Task<bool> SendAsync(string pipeName, string path, TimeSpan timeout)
    {
        byte[] body = Encoding.UTF8.GetBytes(path);
        if (body.Length > MostBytes) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var limit = new CancellationTokenSource(timeout);
            await client.ConnectAsync(limit.Token).ConfigureAwait(false);
            await client.WriteAsync(BitConverter.GetBytes(body.Length), limit.Token).ConfigureAwait(false);
            await client.WriteAsync(body, limit.Token).ConfigureAwait(false);
            await client.FlushAsync(limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException)
        {
            return false;
        }
    }
}

/// <summary>The running STX's end: listens until disposed and hands each path to <c>onPath</c>, on the pipe's thread.</summary>
public sealed class OpenRequestServer : IDisposable
{
    readonly string _name;
    readonly Action<string> _onPath;
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;

    public OpenRequestServer(string pipeName, Action<string> onPath)
    {
        _name = pipeName;
        _onPath = onPath;
        // The pipe exists before the constructor returns, so a second launch that's already waiting connects at once.
        var first = PipeServer.CreatePipe(_name);
        _loop = Task.Run(() => LoopAsync(first));
    }

    async Task LoopAsync(NamedPipeServerStream pipe)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                if (await ReadAsync(pipe).ConfigureAwait(false) is { } path) _onPath(path);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                // A broken or oversized message is dropped; the next launch still gets through.
            }
            await pipe.DisposeAsync().ConfigureAwait(false);
            if (_stop.IsCancellationRequested) break;
            pipe = PipeServer.CreatePipe(_name);
        }
        await pipe.DisposeAsync().ConfigureAwait(false);
    }

    async Task<string?> ReadAsync(NamedPipeServerStream pipe)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        byte[] size = new byte[4];
        await pipe.ReadExactlyAsync(size, limit.Token).ConfigureAwait(false);
        int length = BitConverter.ToInt32(size);
        if (length <= 0 || length > OpenRequests.MostBytes) throw new InvalidDataException($"Refused a {length}-byte open request.");
        byte[] body = new byte[length];
        await pipe.ReadExactlyAsync(body, limit.Token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(body);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _stop.Dispose();
    }
}
