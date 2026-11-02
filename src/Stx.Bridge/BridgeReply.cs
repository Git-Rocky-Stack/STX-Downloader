using System.Text;
using System.Text.Json;

namespace Stx.Bridge;

/// <summary>The answer sent back to the extension. It never contains anything from the message's secrets.</summary>
public static class BridgeReply
{
    public static string Sent(int jobs, int resumed, int alreadyHere = 0) => Write(w =>
    {
        w.WriteBoolean("ok", true);
        w.WriteNumber("jobs", jobs);
        w.WriteNumber("resumed", resumed);
        w.WriteNumber("alreadyHere", alreadyHere);
    });

    public static string Refused(string error, string message) => Write(w =>
    {
        w.WriteBoolean("ok", false);
        w.WriteString("error", error);
        w.WriteString("message", message);
    });

    static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
