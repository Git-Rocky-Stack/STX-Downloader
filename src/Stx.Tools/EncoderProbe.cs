namespace Stx.Tools;

/// <summary>Finds an H.264 encoder that really works here. A hardware encoder counts only after a test encode succeeds.</summary>
public sealed class EncoderProbe(IProcessRunner process, string ffmpegPath)
{
    const string Software = "libx264";
    static readonly string[] Hardware = ["h264_nvenc", "h264_qsv", "h264_amf"];
    static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(20);

    Task<string>? _best;

    public Task<string> BestH264Async() => _best ??= Find();

    Task<HashSet<string>>? _encoders;

    /// <summary>Whether this FFmpeg build has an encoder, from its own list. Asked before a shrink starts (spec assumption A7).</summary>
    public async Task<bool> HasAsync(string encoder) => (await (_encoders ??= ListEncoders()).ConfigureAwait(false)).Contains(encoder);

    async Task<HashSet<string>> ListEncoders()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await process.RunAsync(ffmpegPath, ["-hide_banner", "-nostdin", "-encoders"], line =>
            {
                // " V....D libx264   libx264 H.264 ..." : flags, then the name.
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] is 'V' or 'A' or 'S') lock (names) names.Add(parts[1]);
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        return names;
    }

    async Task<string> Find()
    {
        foreach (string encoder in Hardware)
            if (await Encodes(encoder).ConfigureAwait(false)) return encoder;
        return Software;
    }

    async Task<bool> Encodes(string encoder)
    {
        using var limit = new CancellationTokenSource(TestLimit);
        try
        {
            string[] args = ["-hide_banner", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=640x360:d=0.2", "-c:v", encoder, "-f", "null", "-"];
            return await process.RunAsync(ffmpegPath, args, _ => { }, limit.Token).ConfigureAwait(false) == 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }
}
