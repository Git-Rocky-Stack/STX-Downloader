using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Stx.Playback.Tests;

/// <summary>
/// A web server on this PC for one file, so a stream test needs no internet: it answers GET with the file and the
/// type given, honours "Range: bytes=N-" (LibVLC seeks to an MP4's index at the end), and closes each connection.
/// </summary>
sealed class LocalHttp : IDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource _stop = new();
    readonly byte[] _body;
    readonly string _type;
    int _requests;

    public LocalHttp(string file, string type)
    {
        _body = File.ReadAllBytes(file);
        _type = type;
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/clip";

    /// <summary>How many requests have come in.</summary>
    public int Requests => Volatile.Read(ref _requests);

    async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                long from = 0;
                for (string? line = await reader.ReadLineAsync(); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync())
                {
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase)
                        && long.TryParse(line["Range: bytes=".Length..].Split('-')[0], out long start)) from = start;
                }
                Interlocked.Increment(ref _requests);
                from = Math.Min(from, _body.Length);
                string head = from > 0
                    ? $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{_body.Length - 1}/{_body.Length}\r\n"
                    : "HTTP/1.1 200 OK\r\nAccept-Ranges: bytes\r\n";
                head += $"Content-Type: {_type}\r\nContent-Length: {_body.Length - from}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(_body.AsMemory((int)from));
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                // The player hung up: normal for a stream it stopped reading.
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
