using System.IO.Pipes;

namespace Stx.Bridge;

/// <summary>The native host's end of the pipe. If the app isn't running, it starts it and waits for the pipe to appear.</summary>
public static class PipeClient
{
    static readonly TimeSpan FirstTry = TimeSpan.FromSeconds(1);

    public static async Task<string> SendAsync(string pipeName, string json, Action? launch, TimeSpan timeout)
    {
        using var cancel = new CancellationTokenSource(timeout);
        var started = DateTime.UtcNow;
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            int first = (int)Math.Min(FirstTry.TotalMilliseconds, timeout.TotalMilliseconds / 2);
            if (!await TryConnectAsync(pipe, first).ConfigureAwait(false))
            {
                launch?.Invoke();
                int rest = (int)(timeout - (DateTime.UtcNow - started)).TotalMilliseconds;
                if (rest <= 0 || !await TryConnectAsync(pipe, rest).ConfigureAwait(false)) return NotAvailable();
            }
            await NativeMessaging.WriteAsync(pipe, json, cancel.Token).ConfigureAwait(false);
            return await NativeMessaging.ReadAsync(pipe, cancel.Token).ConfigureAwait(false) ?? NotAvailable();
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return NotAvailable();
        }
    }

    static async Task<bool> TryConnectAsync(NamedPipeClientStream pipe, int milliseconds)
    {
        try
        {
            await pipe.ConnectAsync(milliseconds).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    static string NotAvailable() => BridgeReply.Refused("AppNotAvailable", "STX Downloader didn't answer. Open it and try again.");
}
