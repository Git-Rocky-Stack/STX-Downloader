namespace Stx.Core.Library;

public enum MediaKind { Video, Audio }

/// <summary>
/// The file types STX treats as media: what a watched folder indexes, and what the installer offers to open.
/// Every one is played by a file in the parity corpus (tests/corpus/manifest.json); a test holds the two together.
/// </summary>
public static class MediaTypes
{
    static readonly Dictionary<string, MediaKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".3gp"] = MediaKind.Video, [".asf"] = MediaKind.Video, [".avi"] = MediaKind.Video, [".dv"] = MediaKind.Video,
        [".flv"] = MediaKind.Video, [".m1v"] = MediaKind.Video, [".m2ts"] = MediaKind.Video, [".m2v"] = MediaKind.Video,
        [".m4v"] = MediaKind.Video, [".mkv"] = MediaKind.Video, [".mov"] = MediaKind.Video, [".mp4"] = MediaKind.Video,
        [".mpeg"] = MediaKind.Video, [".mpg"] = MediaKind.Video, [".mts"] = MediaKind.Video, [".mxf"] = MediaKind.Video,
        [".nut"] = MediaKind.Video, [".ogm"] = MediaKind.Video, [".ogv"] = MediaKind.Video, [".rm"] = MediaKind.Video,
        [".rmvb"] = MediaKind.Video, [".ts"] = MediaKind.Video, [".vob"] = MediaKind.Video, [".webm"] = MediaKind.Video,
        [".wmv"] = MediaKind.Video,
        // Audio
        [".aac"] = MediaKind.Audio, [".ac3"] = MediaKind.Audio, [".aif"] = MediaKind.Audio, [".aiff"] = MediaKind.Audio,
        [".ape"] = MediaKind.Audio, [".dts"] = MediaKind.Audio, [".eac3"] = MediaKind.Audio, [".flac"] = MediaKind.Audio,
        [".m4a"] = MediaKind.Audio, [".mid"] = MediaKind.Audio, [".midi"] = MediaKind.Audio, [".mka"] = MediaKind.Audio,
        [".mlp"] = MediaKind.Audio, [".mp2"] = MediaKind.Audio, [".mp3"] = MediaKind.Audio, [".mpc"] = MediaKind.Audio,
        [".oga"] = MediaKind.Audio, [".ogg"] = MediaKind.Audio, [".opus"] = MediaKind.Audio, [".spx"] = MediaKind.Audio,
        [".thd"] = MediaKind.Audio, [".tta"] = MediaKind.Audio, [".voc"] = MediaKind.Audio, [".wav"] = MediaKind.Audio,
        [".wma"] = MediaKind.Audio, [".wv"] = MediaKind.Audio,
    };

    /// <summary>Every media extension, with its dot, in lower case and in order.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Kinds.Keys.Select(k => k.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    public static bool IsMedia(string path) => Kinds.ContainsKey(Path.GetExtension(path));

    public static MediaKind? KindOf(string path) => Kinds.TryGetValue(Path.GetExtension(path), out var kind) ? kind : null;
}
