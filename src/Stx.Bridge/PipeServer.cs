using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Stx.Core.Logging;

namespace Stx.Bridge;

/// <summary>The app's end of the hand-off. Only your own account can connect, it's not reachable over a network, and it opens no port.</summary>
public sealed class PipeServer(string name, Func<string, string> handle, TimeSpan? clientTimeout = null) : IDisposable
{
    readonly TimeSpan _clientTimeout = clientTimeout ?? TimeSpan.FromSeconds(10);

    readonly CancellationTokenSource _stop = new();
    Task? _loop;

    public static string NameForCurrentUser() => $"stx-downloader-{WindowsIdentity.GetCurrent().User}";

    public static NamedPipeServerStream CreatePipe(string name)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    /// <summary>
    /// A fresh pipe for the next client. Creating one can fail for a moment (a handle limit, a pipe a crashed copy left
    /// behind); before the review of 2026-11-07 that ended the listener for the rest of the session. It's tried again,
    /// waiting longer each time up to 30 seconds, until it works or the app closes, when this returns null.
    /// </summary>
    internal static async Task<NamedPipeServerStream?> RecreateAsync(string name, string where, CancellationToken stop)
    {
        var wait = TimeSpan.FromMilliseconds(250);
        while (!stop.IsCancellationRequested)
        {
            try { return CreatePipe(name); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ErrorLog.Write(where, e);
                try { await Task.Delay(wait, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
        }
        return null;
    }

    public void Start()
    {
        // The pipe exists before Start returns, so a client that was waiting for it can connect at once.
        var first = CreatePipe(name);
        _loop = Task.Run(() => LoopAsync(first));
    }

    async Task LoopAsync(NamedPipeServerStream pipe)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                    await ServeAsync(pipe).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException
                    || (e is OperationCanceledException && !_stop.IsCancellationRequested))
                {
                    // One bad or silent client can't stop the next one.
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Nothing a client does may end the listener; what it was is kept for the log.
                    ErrorLog.Write("browser bridge", e);
                }

                // A fresh instance each time, because a pipe left broken by a client can't always be reused.
                if (_stop.IsCancellationRequested) break;
                pipe.Dispose();
                if (await RecreateAsync(name, "browser bridge", _stop.Token).ConfigureAwait(false) is not { } next) return;
                pipe = next;
            }
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        finally
        {
            pipe.Dispose();
        }
    }

    async Task ServeAsync(NamedPipeServerStream pipe)
    {
        string? json;
        using (var reading = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            reading.CancelAfter(_clientTimeout);
            json = await NativeMessaging.ReadAsync(pipe, reading.Token).ConfigureAwait(false);
        }
        if (json is null) return;

        string reply;
        try
        {
            reply = handle(json);
        }
        catch (Exception)
        {
            reply = BridgeReply.Refused("Internal", "STX Downloader couldn't take that item.");
        }

        // The deadline starts here, so time spent handling the message can't lose its answer.
        using var writing = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        writing.CancelAfter(_clientTimeout);
        await NativeMessaging.WriteAsync(pipe, reply, writing.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // It was stopping anyway.
        }
        _stop.Dispose();
    }
}
