using System.IO.Compression;
using System.Security.Cryptography;
using Stx.Core.Logging;

namespace Stx.Tools;

public enum ToolHealth { Current, UpdatePending, Degraded, Missing }

public sealed record ToolOptions(bool UpdatesEnabled);

public sealed record ToolStatus(string Tool, string Stage);

public interface IToolSetup
{
    Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default);
}

/// <summary>The yt-dlp versions on hand, so a run can fall back when a fresh update turns out to be broken.</summary>
public interface IYtDlpVersions
{
    string YtDlpPath { get; }
    string? PreviousYtDlpPath { get; }
    bool UpdateUnproven { get; }
    Task RollbackAsync();
    void MarkUpdateProven();
}

/// <summary>
/// Installs and updates the external tools. Nothing is used unless its published checksum matches and it passes
/// an offline self-test, and every failure leaves what was already working in place. It talks only to the hosts
/// in ToolVersions, and not at all when updates are off. Give it a client that doesn't follow redirects
/// (<see cref="NewHttpClient"/>): it follows them itself, so every hop is held to those hosts.
/// </summary>
public sealed class ToolManager(HttpClient http, string root, ToolOptions options, IProcessRunner process) : IYtDlpVersions, IToolSetup
{
    /// <summary>Can change while running, because Settings can turn updates off.</summary>
    public ToolOptions Options { get; set; } = options;

    static readonly TimeSpan SelfTestLimit = TimeSpan.FromSeconds(30);

    // One install or update at a time: a RETRY during first run used to start a second one beside it.
    readonly SemaphoreSlim _ensuring = new(1, 1);

    string Tool(string name) => Path.Combine(root, name);
    string UnprovenFlag => Tool("update-unproven");
    string RejectedHash => Tool("rejected-hash.txt");
    string Previous => Tool("yt-dlp.previous.exe");
    string Deno => Tool("deno.exe");
    string Ffmpeg => Tool("ffmpeg.exe");
    string Ffprobe => Tool("ffprobe.exe");

    const int MostRedirects = 10;

    /// <summary>The client the updater wants: redirects left to it, so it can check each one.</summary>
    public static HttpClient NewHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("STXDownloader");
        return client;
    }

    // Review 2026-11-07: the hosts were listed but never checked, and redirects went wherever they led. The checksum
    // still guarded what ran; now where it comes from is held to the list too.
    async Task<HttpResponseMessage> GetFromToolHostAsync(string url, HttpCompletionOption completion, CancellationToken cancel)
    {
        var next = new Uri(url);
        for (int hop = 0; ; hop++)
        {
            if (next.Scheme != Uri.UriSchemeHttps || !ToolVersions.UpdateHosts.Contains(next.Host, StringComparer.OrdinalIgnoreCase))
            {
                ErrorLog.Write("tool update", $"refused {next.Scheme}://{next.Host}, which isn't a tool host");
                throw new HttpRequestException($"{next.Host} isn't a tool host.");
            }
            var response = await http.GetAsync(next, completion, cancel).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                response.Dispose();
                if (hop >= MostRedirects) throw new HttpRequestException("Too many redirects.");
                next = location.IsAbsoluteUri ? location : new Uri(next, location);
                continue;
            }
            return response;
        }
    }

    public static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "tools");

    public string YtDlpPath => Tool("yt-dlp.exe");

    public string? FfprobePath => File.Exists(Ffprobe) ? Ffprobe : null;

    public ToolPaths Paths => new(File.Exists(Ffmpeg) ? Ffmpeg : null, File.Exists(Deno) ? Deno : null);

    public string? PreviousYtDlpPath => File.Exists(Previous) ? Previous : null;

    /// <summary>True from an update until the new version completes real work.</summary>
    public bool UpdateUnproven => File.Exists(UnprovenFlag);

    /// <summary>The new yt-dlp did real work. A flag file that can't be deleted right now (an antivirus scan holding
    /// it) stays, and is tried again after the next job: it must never turn a job that worked into one that failed.</summary>
    public void MarkUpdateProven()
    {
        try { File.Delete(UnprovenFlag); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Installs what's missing and updates yt-dlp. Offline or failing, this reports the state and never throws, except when you cancel.</summary>
    public async Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default)
    {
        await _ensuring.WaitAsync(cancel).ConfigureAwait(false);
        try { return await EnsureOnceAsync(progress, cancel).ConfigureAwait(false); }
        finally { _ensuring.Release(); }
    }

    async Task<ToolHealth> EnsureOnceAsync(IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        bool pending = false;

        if (Options.UpdatesEnabled)
        {
            if (File.Exists(YtDlpPath)) pending = await UpdateYtDlpAsync(progress, cancel).ConfigureAwait(false) == ToolHealth.UpdatePending;
            else await InstallYtDlpAsync(progress, cancel).ConfigureAwait(false);

            if (!File.Exists(Ffmpeg) || !File.Exists(Ffprobe)) await InstallFfmpegAsync(progress, cancel).ConfigureAwait(false);
            if (!File.Exists(Deno)) await InstallDenoAsync(progress, cancel).ConfigureAwait(false);
        }

        if (!File.Exists(YtDlpPath) || !File.Exists(Ffmpeg) || !File.Exists(Ffprobe)) return ToolHealth.Missing;
        if (!File.Exists(Deno)) return ToolHealth.Degraded;
        return pending ? ToolHealth.UpdatePending : ToolHealth.Current;
    }

    public Task<ToolHealth> UpdateYtDlpAsync() => UpdateYtDlpAsync(null, default);

    public async Task<ToolHealth> UpdateYtDlpAsync(IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        if (!Options.UpdatesEnabled || !File.Exists(YtDlpPath)) return ToolHealth.Current;

        string? sums = await TextOrNull(ToolVersions.YtDlpSums, cancel).ConfigureAwait(false);
        string? latest = sums is null ? null : Checksums.Find(sums, "yt-dlp.exe");
        if (latest is null) return ToolHealth.Current;
        if (latest == await HashOf(YtDlpPath, cancel).ConfigureAwait(false)) return ToolHealth.Current;
        if (File.Exists(RejectedHash) && File.ReadAllText(RejectedHash).Trim() == latest) return ToolHealth.UpdatePending;

        string fresh = Tool("yt-dlp.new.exe");
        try
        {
            progress?.Report(new ToolStatus("yt-dlp", "Downloading"));
            if (!await DownloadVerified(ToolVersions.YtDlpExe, fresh, latest, cancel).ConfigureAwait(false)) return ToolHealth.UpdatePending;

            progress?.Report(new ToolStatus("yt-dlp", "Testing"));
            if (!await ReportsVersion(fresh, "--version", cancel).ConfigureAwait(false))
            {
                File.WriteAllText(RejectedHash, latest);
                return ToolHealth.UpdatePending;
            }

            // One swap, with the old one kept as the previous version. Two moves left a moment with no yt-dlp.exe at
            // all for a job to start into, and if the second failed, no yt-dlp.exe afterwards (review 2026-11-07).
            File.Delete(Previous);
            File.Replace(fresh, YtDlpPath, Previous, ignoreMetadataErrors: true);
            File.WriteAllText(UnprovenFlag, "");
            progress?.Report(new ToolStatus("yt-dlp", "Ready"));
            return ToolHealth.Current;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A running job can hold a file open. The update simply waits for the next try.
            return ToolHealth.UpdatePending;
        }
        finally
        {
            File.Delete(fresh);
        }
    }

    /// <summary>Goes back to the prior yt-dlp, and won't take this update again. A newer release gets its own chance.</summary>
    /// <remarks>A yt-dlp still running from another job can't be replaced. Then nothing changes, the reason is logged,
    /// and the next job that needs the fallback tries again: a rollback that can't happen yet must not fail the job
    /// whose fallback just worked.</remarks>
    public async Task RollbackAsync()
    {
        if (!File.Exists(Previous)) return;
        try
        {
            string rejected = await HashOf(YtDlpPath, default).ConfigureAwait(false);
            File.Replace(Previous, YtDlpPath, null, ignoreMetadataErrors: true);
            // Only once it's really gone back is this version refused for later.
            File.WriteAllText(RejectedHash, rejected);
            File.Delete(UnprovenFlag);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("yt-dlp rollback", e);
        }
    }

    async Task InstallYtDlpAsync(IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        progress?.Report(new ToolStatus("yt-dlp", "Downloading"));
        string? sums = await TextOrNull(ToolVersions.YtDlpSums, cancel).ConfigureAwait(false);
        string? hash = sums is null ? null : Checksums.Find(sums, "yt-dlp.exe");
        if (hash is null) { progress?.Report(new ToolStatus("yt-dlp", "Failed")); return; }

        string fresh = Tool("yt-dlp.new.exe");
        try
        {
            bool ready = await DownloadVerified(ToolVersions.YtDlpExe, fresh, hash, cancel).ConfigureAwait(false)
                && await ReportsVersion(fresh, "--version", cancel).ConfigureAwait(false);
            if (ready) File.Move(fresh, YtDlpPath);
            progress?.Report(new ToolStatus("yt-dlp", ready ? "Ready" : "Failed"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An antivirus scan holding the file, or a full disk. EnsureAsync promises not to throw for this.
            ErrorLog.Write("yt-dlp install", e);
            progress?.Report(new ToolStatus("yt-dlp", File.Exists(YtDlpPath) ? "Ready" : "Failed"));
        }
        finally
        {
            File.Delete(fresh);
        }
    }

    async Task InstallFfmpegAsync(IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        string? sums = await TextOrNull(FfmpegSource.Sums, cancel).ConfigureAwait(false);
        await InstallFromZip("FFmpeg", FfmpegSource.Zip, sums is null ? null : Checksums.Find(sums, FfmpegSource.ZipName),
            [("ffmpeg.exe", "-version"), ("ffprobe.exe", "-version")], progress, cancel).ConfigureAwait(false);
    }

    async Task InstallDenoAsync(IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        string? sums = await TextOrNull(ToolVersions.DenoSums, cancel).ConfigureAwait(false);
        await InstallFromZip("Deno", ToolVersions.DenoZip, sums is null ? null : Checksums.FindOnly(sums),
            [("deno.exe", "--version")], progress, cancel).ConfigureAwait(false);
    }

    async Task InstallFromZip(string tool, string url, string? hash, (string Exe, string VersionFlag)[] wanted, IProgress<ToolStatus>? progress, CancellationToken cancel)
    {
        progress?.Report(new ToolStatus(tool, "Downloading"));
        if (hash is null) { progress?.Report(new ToolStatus(tool, "Failed")); return; }

        string archive = Tool(tool + ".zip.tmp");
        var extracted = new List<(string Fresh, string Final)>();
        bool ready = false;
        try
        {
            if (await DownloadVerified(url, archive, hash, cancel).ConfigureAwait(false))
            {
                ready = Extract(archive, wanted.Select(w => w.Exe), extracted);
                foreach (var (exe, flag) in wanted)
                {
                    if (!ready) break;
                    ready = await ReportsVersion(Tool(exe + ".new"), flag, cancel).ConfigureAwait(false);
                }
            }
            if (ready) foreach (var (fresh, final) in extracted) File.Move(fresh, final, overwrite: true);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // A damaged archive or a full disk: it just isn't ready.
            ready = false;
        }
        finally
        {
            File.Delete(archive);
            foreach (var (fresh, _) in extracted) File.Delete(fresh);
        }
        progress?.Report(new ToolStatus(tool, ready ? "Ready" : "Failed"));
    }

    // Only named programs come out, written by their plain name, so an entry like ../../evil.exe goes nowhere.
    bool Extract(string archive, IEnumerable<string> names, List<(string Fresh, string Final)> extracted)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (string name in names)
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return false;
            string fresh = Tool(name + ".new");
            entry.ExtractToFile(fresh, overwrite: true);
            extracted.Add((fresh, Tool(name)));
        }
        return true;
    }

    async Task<bool> DownloadVerified(string url, string target, string expectedHash, CancellationToken cancel)
    {
        try
        {
            using var response = await GetFromToolHostAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return false;
            await using (var file = File.Create(target))
                await response.Content.CopyToAsync(file, cancel).ConfigureAwait(false);
            return await HashOf(target, cancel).ConfigureAwait(false) == expectedHash;
        }
        catch (Exception e) when (IsOutageNotCancel(e, cancel))
        {
            return false;
        }
    }

    async Task<string?> TextOrNull(string url, CancellationToken cancel)
    {
        try
        {
            using var response = await GetFromToolHostAsync(url, HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false) : null;
        }
        catch (Exception e) when (IsOutageNotCancel(e, cancel))
        {
            return null;
        }
    }

    static bool IsOutageNotCancel(Exception e, CancellationToken cancel) =>
        !cancel.IsCancellationRequested && e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException;

    /// <summary>The offline self-test: it starts and reports a version. No site is contacted.</summary>
    async Task<bool> ReportsVersion(string exe, string flag, CancellationToken cancel)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(SelfTestLimit);
        bool spoke = false;
        try
        {
            int exit = await process.RunAsync(exe, [flag], line => spoke |= line.Trim().Length > 0, limit.Token).ConfigureAwait(false);
            return exit == 0 && spoke;
        }
        catch (Exception e) when (!cancel.IsCancellationRequested && e is not OutOfMemoryException)
        {
            return false;
        }
    }

    static async Task<string> HashOf(string path, CancellationToken cancel)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false)).ToLowerInvariant();
    }
}
