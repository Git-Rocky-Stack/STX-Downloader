namespace Stx.Tools;

/// <summary>
/// The FFmpeg build. It's the GPL variant because device presets need libx264 when no hardware encoder passes,
/// and the LGPL builds leave it out. See docs/THIRD_PARTY.md.
/// The builds' repository moved from BtbN/FFmpeg-Builds to FFmpeg-Builds/FFmpeg-Builds (GitHub redirects the old
/// name, traced 2026-11-07); STX names the new owner, since a redirect lasts only until the old name is taken.
/// </summary>
public static class FfmpegSource
{
    public const string ZipName = "ffmpeg-n9.0-latest-win64-gpl-9.0.zip";
    public const string Release = "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/latest/";
    public const string Zip = Release + ZipName;
    public const string Sums = Release + "checksums.sha256";
}
