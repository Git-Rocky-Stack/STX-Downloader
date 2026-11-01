using Stx.Core.Errors;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Tools;

public sealed record JobLogLine(long JobId, string Text);

public sealed record JobProgress(long JobId, Progress Progress);

public sealed record SavedFile(long JobId, string Path);

/// <summary>Runs one job through yt-dlp. Every line of output is scrubbed before anything else sees it.</summary>
public sealed class YtDlpRunner(IProcessRunner process, IYtDlpVersions versions, Func<Job, DownloadRequest> requestFor, CaptureVault vault, string? credentialRoot = null) : IJobRunner
{
    const int LinesKeptForDiagnosis = 200;

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
        int exit = await process.RunAsync(path, YtDlpArgs.ForDownload(request), line => OnLine(job.Id, line, recent), cancel).ConfigureAwait(false);
        if (exit == 0) return JobOutcome.Success;
        string evidence;
        lock (recent) evidence = string.Join('\n', recent);
        return JobOutcome.Failure(ErrorClassifier.Classify(evidence, exit));
    }

    // Site handling can break with a new version. A dropped connection, a full disk or DRM can't.
    static bool UpdateCouldCause(ErrorKind? kind) =>
        kind is not (ErrorKind.Network or ErrorKind.OutputFailure or ErrorKind.DrmDetected or ErrorKind.LinkExpired);

    void OnLine(long jobId, string line, Queue<string> recent)
    {
        if (line.StartsWith(YtDlpArgs.FileMarker, StringComparison.Ordinal))
        {
            FileSaved?.Invoke(this, new SavedFile(jobId, line[YtDlpArgs.FileMarker.Length..].Trim()));
            return;
        }

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

    sealed class SingleVersion(string path) : IYtDlpVersions
    {
        public string YtDlpPath => path;
        public string? PreviousYtDlpPath => null;
        public bool UpdateUnproven => false;
        public Task RollbackAsync() => Task.CompletedTask;
        public void MarkUpdateProven() { }
    }
}
