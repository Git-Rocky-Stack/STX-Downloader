namespace Stx.Tools;

/// <summary>
/// The FFmpeg build. It's the GPL variant because device presets need libx264 when no hardware encoder passes,
/// and the LGPL builds leave it out. See docs/THIRD_PARTY.md.
/// </summary>
public static class FfmpegSource
{
    public const string ZipName = "ffmpeg-n9.0-latest-win64-gpl-9.0.zip";
    public const string Zip = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/" + ZipName;
    public const string Sums = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256";
}
