using System.Globalization;
using Stx.Core.Files;
using Stx.Core.Presets;

namespace Stx.Tools;

public enum DownloadMode { Best, AudioOnly, VideoOnly }

public sealed record ToolPaths(string? Ffmpeg, string? JsRuntime);

/// <summary>One download. Url is null for a captured job, whose link stays in its protected config file.</summary>
public sealed record DownloadRequest(string? Url, string OutputFolder, DownloadMode Mode, int? MaxHeight)
{
    public ToolPaths? Tools { get; init; }
    public string? ConfigPath { get; init; }
    public MetadataOptions Metadata { get; init; } = MetadataOptions.None;
    public string OutputTemplate { get; init; } = FilenameTemplate.Default;
}

/// <summary>Builds yt-dlp's arguments. The default keeps the original streams and never re-encodes.</summary>
public static class YtDlpArgs
{
    /// <summary>Prefixes the final file's path in the tool's output, so it can't be mistaken for any other line.</summary>
    public const string FileMarker = "STXFILE:";

    const int ExtensionRoom = 16;

    public static IReadOnlyList<string> ForDownload(DownloadRequest request)
    {
        var args = new List<string> { "--ignore-config", "--encoding", "utf-8" };
        if (request.ConfigPath is not null) args.AddRange(["--config-locations", request.ConfigPath]);
        AddTools(args, request.Tools);
        args.AddRange(
        [
            "--newline",
            "-N", "8",
            "-f", Format(request.Mode, request.MaxHeight),
            "--merge-output-format", "mp4/mkv",
            "-P", request.OutputFolder,
            "-o", FilenameTemplate.ToYtDlp(request.OutputTemplate),
            "--windows-filenames",
            "--trim-filenames", TrimLength(request.OutputFolder).ToString(CultureInfo.InvariantCulture),
            "--no-overwrites",
            "--continue",
            "--print", $"after_move:{FileMarker}%(filepath)s",
            "--no-quiet",
            "--progress",
        ]);
        if (request.Metadata.Tags) args.Add("--embed-metadata");
        if (request.Metadata.CoverArt) args.Add("--embed-thumbnail");
        if (request.Metadata.Chapters) args.Add("--embed-chapters");
        AddUrl(args, request.Url);
        return args;
    }

    /// <summary>Lists a playlist's entries without downloading. Entries it can't reach don't stop the listing.</summary>
    public static IReadOnlyList<string> ForEnumerate(string url, ToolPaths? tools = null)
    {
        var args = new List<string> { "--ignore-config", "--encoding", "utf-8" };
        AddTools(args, tools);
        args.AddRange(["--no-warnings", "-J", "--flat-playlist", "--ignore-errors"]);
        AddUrl(args, url);
        return args;
    }

    /// <summary>Lists the formats of one video, with what's needed to judge its quality.</summary>
    public static IReadOnlyList<string> ForFormats(string url, ToolPaths? tools = null)
    {
        var args = new List<string> { "--ignore-config", "--encoding", "utf-8" };
        AddTools(args, tools);
        args.AddRange(["--no-warnings", "-J", "--no-playlist"]);
        AddUrl(args, url);
        return args;
    }

    // yt-dlp's limit excludes the extension. This leaves room for that and for its .part suffixes.
    static int TrimLength(string folder) => Math.Max(32, Filename.MaxPathLength - folder.Length - 1 - ExtensionRoom);

    static string Format(DownloadMode mode, int? height)
    {
        // <=? keeps streams of unknown height, which is most links captured from a page.
        string limit = height is int h ? $"[height<=?{h}]" : "";
        return mode switch
        {
            DownloadMode.AudioOnly => "ba/b",
            DownloadMode.VideoOnly => $"bv*{limit}",
            _ => $"bv*{limit}+ba/b{limit}",
        };
    }

    static void AddTools(List<string> args, ToolPaths? tools)
    {
        if (tools?.Ffmpeg is { } ffmpeg) args.AddRange(["--ffmpeg-location", ffmpeg]);
        if (tools?.JsRuntime is { } runtime) args.AddRange(["--js-runtimes", $"deno:{runtime}"]);
    }

    // After --, a link beginning with a dash is a link, not an option.
    static void AddUrl(List<string> args, string? url)
    {
        if (url is not null) args.AddRange(["--", url]);
    }
}
