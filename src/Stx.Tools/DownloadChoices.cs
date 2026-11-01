using Stx.Core.Presets;
using Stx.Core.Settings;

namespace Stx.Tools;

/// <summary>Turns what's set in Settings into what yt-dlp is asked for.</summary>
public static class DownloadChoices
{
    public static (DownloadMode Mode, int? MaxHeight) Map(DownloadChoice choice) => choice switch
    {
        DownloadChoice.Up1080 => (DownloadMode.Best, 1080),
        DownloadChoice.Up720 => (DownloadMode.Best, 720),
        DownloadChoice.VideoOnly => (DownloadMode.VideoOnly, null),
        DownloadChoice.AudioOnly => (DownloadMode.AudioOnly, null),
        _ => (DownloadMode.Best, null),
    };

    public static MetadataOptions Metadata(AppSettings settings) => new(settings.EmbedTags, settings.EmbedCoverArt, settings.EmbedChapters);
}
