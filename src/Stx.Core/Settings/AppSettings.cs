using System.Text.Json;
using System.Text.RegularExpressions;
using Stx.Core.Files;
using Stx.Core.Playback;
using Stx.Look;

namespace Stx.Core.Settings;

/// <summary>What a download keeps: the best of everything, a height limit, or one side of the video.</summary>
public enum DownloadChoice { Best, Up1080, Up720, VideoOnly, AudioOnly }

public sealed record AppSettings
{
    public const int MostParallel = 8;

    public LookMode LookMode { get; init; } = LookMode.Dark;
    public int MaxParallel { get; init; } = 3;
    public bool ClipboardDetect { get; init; }
    public bool TrayMode { get; init; }
    public bool StartWithWindows { get; init; }
    public bool ToolUpdates { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public DownloadChoice Download { get; init; } = DownloadChoice.Best;
    public bool EmbedTags { get; init; }
    public bool EmbedCoverArt { get; init; }
    public bool EmbedChapters { get; init; }
    public string OutputFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "STX Downloader");
    public string FilenameTemplate { get; init; } = Files.FilenameTemplate.Default;
    /// <summary>The guide opens by itself once, on the first launch.</summary>
    public bool GuideShown { get; init; }
    /// <summary>How hard playback may work. Auto picks from the PC's hardware.</summary>
    public PlaybackPerformance Performance { get; init; } = PlaybackPerformance.Auto;
    /// <summary>BIT-PERFECT: lossless music holds the sound device exclusively at the file's own format, and films pass
    /// their compressed soundtracks to the receiver. Off by default, because while it's on other apps can't make sound.</summary>
    public bool BitPerfect { get; init; }
    /// <summary>First-run setup asks once whether STX should be the player for videos and music. Yes or Not now, it never asks again.</summary>
    public bool DefaultPlayerAsked { get; init; }
    /// <summary>Folders whose media joins the Library. Null means never chosen: Videos and Music.</summary>
    public IReadOnlyList<string>? WatchedFolders { get; init; }

    /// <summary>The folders actually watched.</summary>
    public IReadOnlyList<string> Watched => WatchedFolders ?? DefaultWatched;

    public static IReadOnlyList<string> DefaultWatched { get; } =
        [.. new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) }.Where(f => f.Length > 0)];

    /// <summary>Brings anything out of range back in, so a hand-edited file can't stop the app.</summary>
    public AppSettings Sanitized()
    {
        var defaults = new AppSettings();
        return this with
        {
            MaxParallel = Math.Clamp(MaxParallel, 1, MostParallel),
            OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? defaults.OutputFolder : OutputFolder,
            FilenameTemplate = string.IsNullOrWhiteSpace(FilenameTemplate) ? defaults.FilenameTemplate : FilenameTemplate,
            Performance = Enum.IsDefined(Performance) ? Performance : PlaybackPerformance.Auto,
            WatchedFolders = CleanFolders(WatchedFolders),
        };
    }

    // The same list comes back untouched, so a save that changes nothing stays a no-op.
    static IReadOnlyList<string>? CleanFolders(IReadOnlyList<string>? folders)
    {
        if (folders is null) return null;
        var clean = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return clean.SequenceEqual(folders, StringComparer.Ordinal) ? folders : clean;
    }
}

public sealed class SettingsStore(string path)
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "settings.json");

    /// <summary>A missing or damaged file means defaults. Settings are never a reason not to start.</summary>
    public AppSettings Load()
    {
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings()).Sanitized();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes a temporary file and swaps it in, so a crash mid-save can't leave a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Pretty));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>The app opens in the last chosen mode. The Windows light or dark setting plays no part.</summary>
public static class LookStartup
{
    public static LookMode Choose(AppSettings settings, bool windowsIsDark) => settings.LookMode;
}

/// <summary>Decides whether copied text is a link worth offering. Only one whole web link counts, never text that mentions one.</summary>
public static partial class LinkDetector
{
    public static bool IsMediaLink(string text)
    {
        string trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || Whitespace().IsMatch(trimmed)) return false;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host.Contains('.');
    }

    [GeneratedRegex(@"\s")]
    private static partial Regex Whitespace();
}
