using System.Buffers.Binary;
using System.Text;

namespace Stx.Bridge;

/// <summary>Chrome's framing: a 4-byte length, then that many bytes of UTF-8 JSON. The pipe uses it too.</summary>
public static class NativeMessaging
{
    /// <summary>Chrome allows 64 MiB, but a cookie jar and a page of links are far smaller, so a bigger claim is a bad frame.</summary>
    public const int MaxIncoming = 8 * 1024 * 1024;

    /// <summary>Chrome's limit on what a host may send back.</summary>
    public const int MaxOutgoing = 1024 * 1024;

    /// <summary>The next message, or null if the stream ended cleanly. A bad frame throws InvalidDataException.</summary>
    public static string? Read(Stream stream) => ReadAsync(stream, CancellationToken.None).GetAwaiter().GetResult();

    public static void Write(Stream stream, string json) => WriteAsync(stream, json, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<string?> ReadAsync(Stream stream, CancellationToken cancel)
    {
        byte[] header = new byte[4];
        int got = await FillAsync(stream, header, cancel).ConfigureAwait(false);
        if (got == 0) return null;
        if (got < 4) throw new InvalidDataException("The message length was cut short.");

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxIncoming) throw new InvalidDataException("The message length is out of range.");

        byte[] body = new byte[length];
        if (await FillAsync(stream, body, cancel).ConfigureAwait(false) < length) throw new InvalidDataException("The message was cut short.");
        return Encoding.UTF8.GetString(body);
    }

    public static async Task WriteAsync(Stream stream, string json, CancellationToken cancel)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        if (body.Length > MaxOutgoing) throw new InvalidDataException("The message is too long to send.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancel).ConfigureAwait(false);
        await stream.WriteAsync(body, cancel).ConfigureAwait(false);
        await stream.FlushAsync(cancel).ConfigureAwait(false);
    }

    static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken cancel)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancel).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
