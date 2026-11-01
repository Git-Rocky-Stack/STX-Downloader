using Stx.Core.Errors;
using Stx.Core.Logging;
using Stx.Core.Queue;

namespace Stx.Tools;

public sealed record JobLogLine(long JobId, string Text);

public sealed record JobProgress(long JobId, Progress Progress);

public sealed record SavedFile(long JobId, string Path);

/// <summary>Runs one job through yt-dlp. Every line of output is scrubbed before anything else sees it.</summary>
public sealed class YtDlpRunner(IProcessRunner process, string ytDlpPath, Func<Job, DownloadRequest> requestFor, CaptureVault vault, string? credentialRoot = null) : IJobRunner
{
    const int LinesKeptForDiagnosis = 200;

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

        var recent = new Queue<string>();
        try
        {
            int exit = await process.RunAsync(ytDlpPath, YtDlpArgs.ForDownload(request), line => OnLine(job.Id, line, recent), cancel).ConfigureAwait(false);
            if (exit == 0) return JobOutcome.Success;
            string evidence;
            lock (recent) evidence = string.Join('\n', recent);
            return JobOutcome.Failure(ErrorClassifier.Classify(evidence, exit));
        }
        finally
        {
            credentials?.Dispose();
        }
    }

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
}
