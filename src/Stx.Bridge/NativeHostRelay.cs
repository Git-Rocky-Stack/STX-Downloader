namespace Stx.Bridge;

/// <summary>What the native host does: one message in from the browser, across the pipe, and the answer back. It reads none of it.</summary>
public static class NativeHostRelay
{
    public static async Task RunAsync(Stream input, Stream output, string pipeName, Action? launch, TimeSpan timeout)
    {
        string reply;
        try
        {
            if (await NativeMessaging.ReadAsync(input, CancellationToken.None).ConfigureAwait(false) is not { } json) return;
            reply = await PipeClient.SendAsync(pipeName, json, launch, timeout).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            reply = BridgeReply.Refused("Malformed", "The extension sent something the app couldn't read.");
        }
        await NativeMessaging.WriteAsync(output, reply, CancellationToken.None).ConfigureAwait(false);
    }
}
