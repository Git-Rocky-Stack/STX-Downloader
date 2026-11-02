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
}

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

        if (request.Reencode) AddDeviceVideo(args, request, h264Encoder);
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
