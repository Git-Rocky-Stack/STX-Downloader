using Stx.Core.Errors;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Tools;

public sealed record JobLogLine(long JobId, string Text);

public sealed record JobProgress(long JobId, Progress Progress);

/// <summary>Reused means yt-dlp found the file already there and wrote nothing.</summary>
public sealed record SavedFile(long JobId, string Path, bool Reused = false);

/// <summary>Runs one job through yt-dlp. Every line of output is scrubbed before anything else sees it.</summary>
public sealed class YtDlpRunner(IProcessRunner process, IYtDlpVersions versions, Func<Job, DownloadRequest> requestFor, CaptureVault vault, string? credentialRoot = null) : IJobRunner
{
    const int LinesKeptForDiagnosis = 200;
    const string AlreadyThere = " has already been downloaded";
    static readonly TimeSpan TimeResolution = TimeSpan.FromSeconds(2);

    readonly Mp4Preference _mp4 = new(process);

    public YtDlpRunner(IProcessRunner process, string ytDlpPath, Func<Job, DownloadRequest> requestFor, CaptureVault vault, string? credentialRoot = null)
        : this(process, new SingleVersion(ytDlpPath), requestFor, vault, credentialRoot)
    {
    }

    /// <summary>A fresh yt-dlp failed on a site and the previous one worked, so it's been put back. The user gets told.</summary>
    public event EventHandler? RolledBack;

    public event EventHandler<JobLogLine>? LogLine;
    public event EventHandler<JobProgress>? ProgressChanged;
    public event EventHandler<SavedFile>? FileSaved;

    public async Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
    {
        var request = requestFor(job);
        if (!FolderIsUsable(request.OutputFolder)) return JobOutcome.Failure(ErrorKind.OutputFailure);

        CredentialFile? credentials = null;
        if (job.Kind == JobKind.Captured)
        {
            if (vault.Get(job.Id) is not { } secrets) return JobOutcome.Failure(ErrorKind.LinkExpired);
            credentials = CredentialFile.Create(secrets.Cookies, secrets.Headers, secrets.StreamUrl, credentialRoot);
            request = request with { ConfigPath = credentials.ConfigPath };
        }

        try
        {
            var outcome = await RunOnce(versions.YtDlpPath, job, request, cancel).ConfigureAwait(false);
            if (outcome.IsSuccess)
            {
                if (versions.UpdateUnproven) versions.MarkUpdateProven();
                return outcome;
            }

            if (versions.UpdateUnproven && versions.PreviousYtDlpPath is { } previous && UpdateCouldCause(outcome.Error))
            {
                var second = await RunOnce(previous, job, request, cancel).ConfigureAwait(false);
                if (second.IsSuccess)
                {
                    await versions.RollbackAsync().ConfigureAwait(false);
                    RolledBack?.Invoke(this, EventArgs.Empty);
                    return second;
                }
            }
            return outcome;
        }
        finally
        {
            credentials?.Dispose();
        }
    }

    async Task<JobOutcome> RunOnce(string path, Job job, DownloadRequest request, CancellationToken cancel)
    {
        var recent = new Queue<string>();
        var run = new OneRun();
        var started = DateTime.UtcNow;
        int exit = await process.RunAsync(path, YtDlpArgs.ForDownload(request), line => OnLine(job.Id, line, recent, run), cancel).ConfigureAwait(false);

        // That sentence is yt-dlp's to reword. A file this run wrote also has a fresh time, because it runs with --no-mtime.
        if (exit == 0 && run.Saved.Any(p => WrittenBefore(p, started))) run.Reused = true;

        // yt-dlp exits happily when a name is taken, and reports the file that's there, which may be a different video's.
        if (exit == 0 && run.Reused && request.UniqueTemplate is { } unique && !run.Saved.All(p => request.IsOwnFile?.Invoke(p) == true))
        {
            LogLine?.Invoke(this, new JobLogLine(job.Id, "That file name is taken, so this one gets its id added to its name."));
            return await RunOnce(path, job, request with { OutputTemplate = unique, UniqueTemplate = null }, cancel).ConfigureAwait(false);
        }

        foreach (string saved in run.Saved)
        {
            string final = exit == 0 && !run.Reused ? await _mp4.PreferAsync(saved, request.Tools?.Ffmpeg, cancel).ConfigureAwait(false) : saved;
            FileSaved?.Invoke(this, new SavedFile(job.Id, final, run.Reused));
        }

        if (exit == 0) return JobOutcome.Success;
        string evidence;
        lock (recent) evidence = string.Join('\n', recent);
        return JobOutcome.Failure(ErrorClassifier.Classify(evidence, exit));
    }

    // FAT and exFAT drives keep write times to the nearest two seconds.
    static bool WrittenBefore(string path, DateTime started) =>
        File.Exists(path) && File.GetLastWriteTimeUtc(path) < started - TimeResolution;

    // Site handling can break with a new version. A dropped connection, a full disk or DRM can't.
    static bool UpdateCouldCause(ErrorKind? kind) =>
        kind is not (ErrorKind.Network or ErrorKind.OutputFailure or ErrorKind.DrmDetected or ErrorKind.LinkExpired);

    void OnLine(long jobId, string line, Queue<string> recent, OneRun run)
    {
        if (line.StartsWith(YtDlpArgs.FileMarker, StringComparison.Ordinal))
        {
            lock (run) run.Saved.Add(line[YtDlpArgs.FileMarker.Length..].Trim());
            return;
        }
        if (line.StartsWith("[download]", StringComparison.Ordinal) && line.TrimEnd().EndsWith(AlreadyThere, StringComparison.Ordinal)) run.Reused = true;

        string clean = Redactor.Scrub(line);
        lock (recent)
        {
            recent.Enqueue(clean);
            if (recent.Count > LinesKeptForDiagnosis) recent.Dequeue();
        }
        LogLine?.Invoke(this, new JobLogLine(jobId, clean));
        if (YtDlpProgress.TryParse(clean, out var progress)) ProgressChanged?.Invoke(this, new JobProgress(jobId, progress));
    }

    // Finding this out now gives a plain answer, where a pulled drive would otherwise fail deep in the tool.
    static bool FolderIsUsable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // What one run of the tool said. Files are held until it exits, because only then is it known whether they count.
    sealed class OneRun
    {
        public List<string> Saved { get; } = [];
        public volatile bool Reused;
    }

    sealed class SingleVersion(string path) : IYtDlpVersions
    {
        public string YtDlpPath => path;
        public string? PreviousYtDlpPath => null;
        public bool UpdateUnproven => false;
        public Task RollbackAsync() => Task.CompletedTask;
        public void MarkUpdateProven() { }
    }
}
