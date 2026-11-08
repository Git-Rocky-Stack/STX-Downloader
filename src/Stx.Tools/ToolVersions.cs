namespace Stx.Tools;

/// <summary>Where each tool comes from. These hosts are the only ones the updater ever contacts: ToolManager follows
/// each redirect itself and refuses one that leaves them, or leaves HTTPS.</summary>
public static class ToolVersions
{
    public const string YtDlpExe = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    public const string YtDlpSums = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";

    /// <summary>yt-dlp needs a JavaScript runtime for full YouTube extraction. Without one it loses formats, such as 5.1 audio.</summary>
    public const string DenoZip = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
    public const string DenoSums = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum";

    /// <summary>GitHub sends release downloads on to these hosts (traced 2026-11-07: github.com, then
    /// release-assets.githubusercontent.com).</summary>
    public static readonly IReadOnlyList<string> UpdateHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];
}
