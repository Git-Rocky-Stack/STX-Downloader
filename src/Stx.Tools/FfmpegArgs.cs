using Stx.Core.Audio;
using Stx.Core.Presets;

namespace Stx.Tools;

/// <summary>A conversion. The input is only ever read: the result is always a new file.</summary>
public sealed record ConvertRequest(string InputPath, string OutputFolder, OutputChoice Output)
{
    public bool Reencode { get; init; }
    public int? MaxHeight { get; init; }
    public bool LevelLoudness { get; init; }
    public MetadataOptions Metadata { get; init; } = MetadataOptions.None;

    /// <summary>True only when the input is known to hold a picture. In a video file, the first video stream is the movie.</summary>
    public bool InputHasCoverArt { get; init; }

    public double? DurationSeconds { get; init; }

    /// <summary>A shrink copy (Phase 7): the codec to make it with. Null for every other conversion.</summary>
    public ShrinkCodec? Shrink { get; init; }

    /// <summary>Added to the copy's file name, before its extension.</summary>
    public string? Suffix { get; init; }

    /// <summary>A shrink's audio streams' channel layouts, in order, so a layout Opus refuses can be widened first.</summary>
    public IReadOnlyList<SoundLayout> AudioLayouts { get; init; } = [];
}

/// <summary>An audio stream's channel count and ffprobe's name for its layout.</summary>
public sealed record SoundLayout(int Channels, string Layout);

public static class FfmpegArgs
{
    const string LevelFilter = "loudnorm=I=-14:TP=-1.0:LRA=11";

    public static string Extension(OutputChoice output) => output switch
    {
        OutputChoice.Flac => ".flac",
        OutputChoice.Alac or OutputChoice.AacM4a => ".m4a",
        OutputChoice.Wav => ".wav",
        OutputChoice.Aiff => ".aiff",
        OutputChoice.Mp3 => ".mp3",
        OutputChoice.Opus => ".opus",
        OutputChoice.OggVorbis => ".ogg",
        OutputChoice.Mkv => ".mkv",
        OutputChoice.Mp4 => ".mp4",
        _ => "",
    };

    public static IReadOnlyList<string> ForConvert(ConvertRequest request, string target, string h264Encoder)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-progress", "pipe:1", "-nostats", "-i", request.InputPath };
        args.AddRange(["-map_metadata", request.Metadata.Tags ? "0" : "-1"]);
        args.AddRange(["-map_chapters", request.Metadata.Chapters ? "0" : "-1"]);

        if (request.Shrink is { } codec) AddShrink(args, request, codec);
        else if (request.Reencode) AddDeviceVideo(args, request, h264Encoder);
        else if (request.Output is OutputChoice.Mp4 or OutputChoice.Mkv or OutputChoice.Original) args.AddRange(["-map", "0", "-dn", "-c", "copy"]);
        else AddAudio(args, request);

        args.Add(target);
        return args;
    }

    static void AddDeviceVideo(List<string> args, ConvertRequest request, string encoder)
    {
        args.AddRange(["-map", "0:v:0", "-map", "0:a?", "-c:v", encoder]);
        if (encoder == "libx264") args.AddRange(["-preset", "medium", "-crf", "20"]);
        else args.AddRange(["-b:v", "8M"]);
        if (request.MaxHeight is int height) args.AddRange(["-vf", $"scale=-2:'min({height},ih)'"]);
        args.AddRange(["-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]);
        if (request.LevelLoudness) args.AddRange(["-af", LevelFilter]);
    }

    /// <summary>The encoder each shrink codec needs, to check this FFmpeg has it first.</summary>
    public static string Encoder(ShrinkCodec codec) => codec switch
    {
        ShrinkCodec.Av1 => "libsvtav1",
        ShrinkCodec.Hevc => "libx265",
        _ => "libopus",
    };

    /// <summary>
    /// The filter a sound needs before Opus, or null when Opus takes it as it is. Checked against FFmpeg 9.0: with
    /// mapping family 1, libopus takes only Vorbis's layouts (mono, stereo, 3.0, quad, 5.0, 5.1, 6.1, 7.1) and refuses
    /// every other, including 5.1(side), which most films' AC3 and DTS arrive as.
    /// The side layouts hold Vorbis's channels in Vorbis's order, so they're renamed and no sample changes. The rest are
    /// widened to the smallest Vorbis layout holding each of their channels: a channel the source lacks is silent, and only
    /// a channel Vorbis has no place for (a back centre, front wides) is shared between its two neighbours.
    /// An unnamed layout (PCM in Matroska) with 3 or 4 channels is refused too; 5.1 holds any arrangement of 3 or 4.
    /// </summary>
    public static string? OpusFilter(SoundLayout sound) => sound.Layout switch
    {
        "5.1(side)" => "channelmap=channel_layout=5.1",
        "5.0(side)" => "channelmap=channel_layout=5.0",
        "quad(side)" => "channelmap=channel_layout=quad",
        "3.0(back)" => Widen("quad"),
        "4.0" => Widen("5.0"),
        "2.1" or "3.1" or "4.1" => Widen("5.1"),
        "6.0" => Widen("6.1"),
        "6.0(front)" or "hexagonal" or "6.1(back)" or "6.1(front)" or "7.0" or "7.0(front)" or "7.1(wide)" or "7.1(wide-side)" or "octagonal" => Widen("7.1"),
        "" or "unknown" when sound.Channels is 3 or 4 => Widen("5.1"),
        _ => null,
    };

    static string Widen(string layout) => "aformat=channel_layouts=" + layout;

    // Picture and sound in modern codecs at settings that keep the look, every subtitle, chapter and attachment carried
    // over. 10-bit output, so a 10-bit or HDR source isn't cut to 8 bits. Opus takes only Vorbis's layouts; any other
    // is renamed or widened first (OpusFilter).
    static void AddShrink(List<string> args, ConvertRequest request, ShrinkCodec codec)
    {
        for (int i = 0; i < request.AudioLayouts.Count; i++)
            if (OpusFilter(request.AudioLayouts[i]) is { } filter) args.AddRange([$"-filter:a:{i}", filter]);
        var opus = new[] { "-c:a", "libopus", "-b:a", codec == ShrinkCodec.Opus ? "128k" : "160k", "-mapping_family", "1" };
        if (codec == ShrinkCodec.Opus)
        {
            args.AddRange(["-map", "0:a", "-vn", "-sn", .. opus]);
            return;
        }
        // MP4's own subtitle format doesn't go into MKV; it's turned into SRT. Everything else is copied as it is.
        bool mp4Text = Path.GetExtension(request.InputPath).ToLowerInvariant() is ".mp4" or ".m4v" or ".mov";
        args.AddRange(["-map", "0:v:0", "-map", "0:a?", "-map", "0:s?", "-map", "0:t?"]);
        args.AddRange(codec == ShrinkCodec.Av1
            ? ["-c:v", "libsvtav1", "-preset", "6", "-crf", "30", "-pix_fmt", "yuv420p10le"]
            : ["-c:v", "libx265", "-preset", "medium", "-crf", "24", "-pix_fmt", "yuv420p10le", "-x265-params", "log-level=error"]);
        args.AddRange(opus);
        args.AddRange(["-c:s", mp4Text ? "srt" : "copy", "-c:t", "copy"]);
    }

    static void AddAudio(List<string> args, ConvertRequest request)
    {
        bool keepCover = request.Metadata.CoverArt && request.InputHasCoverArt && HoldsCoverArt(request.Output);
        args.AddRange(["-map", "0:a"]);
        if (keepCover) args.AddRange(["-map", "0:v:0", "-c:v", "copy", "-disposition:v", "attached_pic"]);
        else args.Add("-vn");

        args.AddRange(request.Output switch
        {
            OutputChoice.Flac => ["-c:a", "flac", "-compression_level", "8"],
            OutputChoice.Alac => ["-c:a", "alac"],
            // 24-bit, so decoding a lossy source adds no rounding of its own.
            OutputChoice.Wav => ["-c:a", "pcm_s24le"],
            OutputChoice.Aiff => ["-c:a", "pcm_s24be"],
            OutputChoice.AacM4a => ["-c:a", "aac", "-b:a", "256k"],
            OutputChoice.Mp3 => ["-c:a", "libmp3lame", "-q:a", "0"],
            OutputChoice.Opus => ["-c:a", "libopus", "-b:a", "192k"],
            _ => (string[])["-c:a", "libvorbis", "-q:a", "8"],
        });
        if (request.LevelLoudness) args.AddRange(["-af", LevelFilter]);
    }

    static bool HoldsCoverArt(OutputChoice output) => output is OutputChoice.Flac or OutputChoice.Mp3 or OutputChoice.AacM4a or OutputChoice.Alac;
}
