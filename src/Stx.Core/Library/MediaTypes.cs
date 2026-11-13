namespace Stx.Core.Library;

public enum MediaKind { Video, Audio }

/// <summary>
/// The file types STX treats as media: what a watched folder indexes, and what the installer offers to open.
/// Every one is played by a file in the parity corpus (tests/corpus/manifest.json); a test holds the two together.
/// </summary>
/// <remarks>
/// A type is left out when the engine doesn't play it (DSD's .dsf and .dff, raw MPEG-4 video as .mp4v, gzipped
/// .vgz), when it is protected (.m4p), or when the extension names something else as often (.xa, .sid, .spc, .nsf,
/// .stm, .ps, .rec, .bin, .sub, .idx). .mod is left out too, though the corpus proves the engine plays both of its
/// kinds: it names tracker music, a camcorder's MPEG-2 recording and every Go project's go.mod alike, so registered as
/// either kind it would mislabel the other (a camcorder .mod would shrink to sound alone) and list go.mod files in a
/// watched folder as music. .ogx is video: Ogg's extension for multiplexed files, which carry pictures as well as sound.
/// </remarks>
public static class MediaTypes
{
    static readonly Dictionary<string, MediaKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Video
        [".264"] = MediaKind.Video, [".265"] = MediaKind.Video, [".3g2"] = MediaKind.Video, [".3gp"] = MediaKind.Video,
        [".3gp2"] = MediaKind.Video, [".3gpp"] = MediaKind.Video, [".amv"] = MediaKind.Video, [".asf"] = MediaKind.Video,
        [".avi"] = MediaKind.Video, [".bik"] = MediaKind.Video, [".divx"] = MediaKind.Video, [".dv"] = MediaKind.Video,
        [".dvr-ms"] = MediaKind.Video, [".evo"] = MediaKind.Video, [".f4v"] = MediaKind.Video, [".flv"] = MediaKind.Video,
        [".gxf"] = MediaKind.Video, [".h264"] = MediaKind.Video, [".h265"] = MediaKind.Video, [".hevc"] = MediaKind.Video,
        [".ivf"] = MediaKind.Video, [".m1v"] = MediaKind.Video, [".m2p"] = MediaKind.Video, [".m2t"] = MediaKind.Video,
        [".m2ts"] = MediaKind.Video, [".m2v"] = MediaKind.Video, [".m4v"] = MediaKind.Video, [".mjpeg"] = MediaKind.Video,
        [".mjpg"] = MediaKind.Video, [".mk3d"] = MediaKind.Video, [".mkv"] = MediaKind.Video, [".mov"] = MediaKind.Video,
        [".mp2v"] = MediaKind.Video, [".mp4"] = MediaKind.Video, [".mpe"] = MediaKind.Video, [".mpeg"] = MediaKind.Video,
        [".mpg"] = MediaKind.Video, [".mpv"] = MediaKind.Video, [".mpv2"] = MediaKind.Video, [".mts"] = MediaKind.Video,
        [".mxf"] = MediaKind.Video, [".nsv"] = MediaKind.Video, [".nut"] = MediaKind.Video, [".nuv"] = MediaKind.Video,
        [".ogm"] = MediaKind.Video, [".ogv"] = MediaKind.Video, [".ogx"] = MediaKind.Video, [".pva"] = MediaKind.Video,
        [".qt"] = MediaKind.Video, [".rm"] = MediaKind.Video, [".rmvb"] = MediaKind.Video, [".tod"] = MediaKind.Video,
        [".tp"] = MediaKind.Video, [".trp"] = MediaKind.Video, [".ts"] = MediaKind.Video, [".vob"] = MediaKind.Video,
        [".vro"] = MediaKind.Video, [".webm"] = MediaKind.Video, [".wm"] = MediaKind.Video, [".wmv"] = MediaKind.Video,
        [".wtv"] = MediaKind.Video, [".xvid"] = MediaKind.Video, [".y4m"] = MediaKind.Video,
        // Audio
        [".3ga"] = MediaKind.Audio, [".669"] = MediaKind.Audio, [".a52"] = MediaKind.Audio, [".aac"] = MediaKind.Audio,
        [".ac3"] = MediaKind.Audio, [".adts"] = MediaKind.Audio, [".aif"] = MediaKind.Audio, [".aifc"] = MediaKind.Audio,
        [".aiff"] = MediaKind.Audio, [".amr"] = MediaKind.Audio, [".ape"] = MediaKind.Audio, [".au"] = MediaKind.Audio,
        [".awb"] = MediaKind.Audio, [".caf"] = MediaKind.Audio, [".dts"] = MediaKind.Audio, [".eac3"] = MediaKind.Audio,
        [".flac"] = MediaKind.Audio, [".gbs"] = MediaKind.Audio, [".gsm"] = MediaKind.Audio, [".it"] = MediaKind.Audio,
        [".kar"] = MediaKind.Audio, [".m4a"] = MediaKind.Audio, [".m4b"] = MediaKind.Audio, [".m4r"] = MediaKind.Audio,
        [".mid"] = MediaKind.Audio, [".midi"] = MediaKind.Audio, [".mka"] = MediaKind.Audio, [".mlp"] = MediaKind.Audio,
        [".mp1"] = MediaKind.Audio, [".mp2"] = MediaKind.Audio, [".mp3"] = MediaKind.Audio,
        [".mpa"] = MediaKind.Audio, [".mpc"] = MediaKind.Audio, [".mpga"] = MediaKind.Audio, [".mtm"] = MediaKind.Audio,
        [".oga"] = MediaKind.Audio, [".ogg"] = MediaKind.Audio, [".opus"] = MediaKind.Audio, [".qcp"] = MediaKind.Audio,
        [".ra"] = MediaKind.Audio, [".rmi"] = MediaKind.Audio, [".s3m"] = MediaKind.Audio, [".shn"] = MediaKind.Audio,
        [".snd"] = MediaKind.Audio, [".spx"] = MediaKind.Audio, [".tak"] = MediaKind.Audio, [".thd"] = MediaKind.Audio,
        [".tta"] = MediaKind.Audio, [".vgm"] = MediaKind.Audio, [".voc"] = MediaKind.Audio, [".vqf"] = MediaKind.Audio,
        [".w64"] = MediaKind.Audio, [".wav"] = MediaKind.Audio, [".weba"] = MediaKind.Audio, [".wma"] = MediaKind.Audio,
        [".wv"] = MediaKind.Audio, [".xm"] = MediaKind.Audio,
    };

    /// <summary>Every media extension, with its dot, in lower case and in order.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Kinds.Keys.Select(k => k.ToLowerInvariant()).Order(StringComparer.Ordinal)];

    public static bool IsMedia(string path) => Kinds.ContainsKey(Path.GetExtension(path));

    public static MediaKind? KindOf(string path) => Kinds.TryGetValue(Path.GetExtension(path), out var kind) ? kind : null;
}
