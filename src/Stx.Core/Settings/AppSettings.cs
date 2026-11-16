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
    /// <summary>ENHANCE: OFF, SHARPEN or UPSCALE, for the picture on screen only.</summary>
    public EnhanceLevel Enhance { get; init; } = EnhanceLevel.Off;
    /// <summary>BIT-PERFECT: lossless music holds the sound device exclusively at the file's own format, and films pass
    /// their compressed soundtracks to the receiver. Off by default, because while it's on other apps can't make sound.</summary>
    public bool BitPerfect { get; init; }
    /// <summary>First-run setup asks once whether STX should be the player for videos and music. Yes or Not now, it never asks again.</summary>
    public bool DefaultPlayerAsked { get; init; }
    /// <summary>The tutorial chapters finished to their last step, by id. Each one's lamp on the Tutorials plate lights green.</summary>
    public IReadOnlyList<string>? TutorialsDone { get; init; }
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
            Enhance = Enum.IsDefined(Enhance) ? Enhance : EnhanceLevel.Off,
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

    /// <summary>Where a damaged settings file is kept, so the first save after it doesn't erase what it held.</summary>
    public string DamagedPath => path + ".damaged";

    // Eight tries, 25 ms more apart each time: 0.7 s of waiting in all.
    const int SwapAttempts = 8;

    /// <summary>
    /// A missing or damaged file means defaults. Settings are never a reason not to start. A damaged one is kept
    /// beside it as settings.json.damaged and the error log says so: before the review of 2026-11-07 the next save
    /// simply wrote the defaults over it.
    /// </summary>
    public AppSettings Load()
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(text) ?? throw new JsonException("The settings file holds null.")).Sanitized();
        }
        catch (JsonException e)
        {
            Stx.Core.Logging.ErrorLog.Write("settings", $"{path} couldn't be read ({e.Message}); kept as {DamagedPath}, and defaults used");
            try { File.Copy(path, DamagedPath, overwrite: true); }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { }
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes a temporary file, flushed to the disk itself, then swaps it in, so neither a crash nor a power cut
    /// mid-save can leave a half-written or empty file. Each save has its own temporary name, so two can't collide.
    /// Windows refuses the swap while another process has the old file open, as a virus scanner reading a file just
    /// written does, so that is waited out, a little longer each time, for under a second before the save gives up.
    /// </summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, settings, Pretty);
                file.Flush(flushToDisk: true);
            }
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite: true);
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < SwapAttempts)
                {
                    Thread.Sleep(25 * attempt);
                }
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
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
