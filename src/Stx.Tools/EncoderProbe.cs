namespace Stx.Tools;

/// <summary>Finds an H.264 encoder that really works here. A hardware encoder counts only after a test encode succeeds.</summary>
public sealed class EncoderProbe(IProcessRunner process, string ffmpegPath)
{
    const string Software = "libx264";
    static readonly string[] Hardware = ["h264_nvenc", "h264_qsv", "h264_amf"];
    static readonly TimeSpan TestLimit = TimeSpan.FromSeconds(20);

    Task<string>? _best;

    public Task<string> BestH264Async() => _best ??= Find();

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
