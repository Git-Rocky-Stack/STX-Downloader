using Stx.Core.Errors;
using Stx.Core.Jobs;

namespace Stx.Core.Queue;

/// <summary>
/// Runs jobs through their states, keeping the database as the record. Nothing moves except through
/// JobTransitions. A job whose run was replaced, by a pause or cancel, cannot report back and change anything.
/// </summary>
public sealed class QueueEngine(IJobRepo repo, IJobRunner runner, QueuePolicy policy, IClock clock, CaptureVault vault)
{
    readonly object _gate = new();
    readonly Dictionary<long, CancellationTokenSource> _running = new();
    readonly Dictionary<long, DateTimeOffset> _retryAt = new();
    readonly Dictionary<long, Task> _drives = new();
    bool _stopping;

    public event EventHandler<Job>? JobChanged;

    /// <summary>Takes effect at the next start. Settings can change this while jobs run.</summary>
    public QueuePolicy Policy { get; set; } = policy;

    public IReadOnlyList<Job> AllJobs()
    {
        lock (_gate) return repo.All();
    }

    /// <summary>Adds a job and starts it when a slot is free. A conversion already waiting or running for the same
    /// file and preset isn't added again; that job is returned instead.</summary>
    public Job Enqueue(long mediaId, JobKind kind, string preset, CaptureSecrets? secrets = null)
    {
        lock (_gate)
        {
            // Review 2026-11-07: SHRINK pressed twice, or a file shrunk and then its folder, queued the same
            // multi-hour encode twice; the second copy then landed as a second row of the same format.
            if (kind == JobKind.Convert && Pending(mediaId, kind, preset) is { } already) return already;
            var job = repo.Add(new Job(0, mediaId, kind, JobState.Queued, 0, null, preset, clock.Now));
            if (secrets is not null) vault.Put(job.Id, secrets);
            JobChanged?.Invoke(this, job);
            Pump();
            return repo.Get(job.Id) ?? job;
        }
    }

    /// <summary>True when a job of this kind, for this item and preset, is waiting, paused or running.</summary>
    public bool HasPending(long mediaId, JobKind kind, string preset)
    {
        lock (_gate) return Pending(mediaId, kind, preset) is not null;
    }

    Job? Pending(long mediaId, JobKind kind, string preset) =>
        repo.All().FirstOrDefault(j => j.MediaId == mediaId && j.Kind == kind && j.State is JobState.Queued or JobState.Running or JobState.Paused or JobState.Waiting or JobState.Analyzing
            && string.Equals(j.Preset, preset, StringComparison.Ordinal));

    public bool Pause(long id) => Act(id, job =>
    {
        if (!Move(job, JobState.Paused)) return false;
        StopRun(id);
        _retryAt.Remove(id);
        return true;
    });

    public bool Resume(long id) => Act(id, job => job.State == JobState.Paused && Move(job, JobState.Queued));

    public bool Cancel(long id) => Act(id, job =>
    {
        if (!Move(job, JobState.Canceled)) return false;
        StopRun(id);
        _retryAt.Remove(id);
        vault.Forget(id);
        return true;
    });

    /// <summary>A job is finished when nothing more happens to it unless asked: complete, canceled, or failed.</summary>
    public static bool IsFinished(JobState state) => state is JobState.Completed or JobState.Canceled or JobState.Failed;

    /// <summary>
    /// A finished job left the queue for good, by CLEAR or CLEAR DONE. Its row is gone from the database; what it saved
    /// is still in the Library and on disk.
    /// </summary>
    public event EventHandler<long>? JobRemoved;

    /// <summary>
    /// Takes one finished job off the queue for good. False for a job still under way (it must be canceled first) and
    /// for one whose last run is still ending, so a tool can never report into a row that's gone.
    /// </summary>
    public bool Remove(long id)
    {
        lock (_gate) return RemoveFinishedJob(id);
    }

    /// <summary>Takes every finished job off the queue (CLEAR DONE). Returns how many went.</summary>
    public int RemoveFinished()
    {
        lock (_gate) return repo.All().Where(j => IsFinished(j.State)).Count(j => RemoveFinishedJob(j.Id));
    }

    bool RemoveFinishedJob(long id)
    {
        if (repo.Get(id) is not { } job || !IsFinished(job.State) || _drives.ContainsKey(id) || _running.ContainsKey(id)) return false;
        repo.Remove(id);
        _retryAt.Remove(id);
        vault.Forget(id);
        JobRemoved?.Invoke(this, id);
        return true;
    }

    public bool Retry(long id) => Act(id, job =>
        job.State is JobState.Failed or JobState.Canceled && Move(job with { Attempts = 0, Error = null }, JobState.Queued));

    /// <summary>A fresh capture for a job whose link expired.</summary>
    public bool Recapture(long id, CaptureSecrets secrets) => Act(id, job =>
    {
        if (job.State != JobState.NeedsRecapture || !Move(job with { Attempts = 0, Error = null }, JobState.Queued)) return false;
        vault.Put(id, secrets);
        return true;
    });

    /// <summary>
    /// Call once at startup. Nothing can really be running, so active jobs go back in line. A captured job's
    /// secrets died with the old process, so any unfinished one must be captured again.
    /// </summary>
    public void RecoverAfterRestart()
    {
        lock (_gate)
        {
            foreach (var job in repo.All())
            {
                bool unfinished = job.State is JobState.Queued or JobState.Running or JobState.Paused or JobState.Waiting or JobState.Analyzing;
                if (job.Kind == JobKind.Captured && unfinished && vault.Get(job.Id) is null) Move(job, JobState.NeedsRecapture);
                else if (job.State is JobState.Running or JobState.Waiting or JobState.Analyzing) Move(job, JobState.Queued);
            }
            Pump();
        }
    }

    /// <summary>
    /// The app is quitting: stops every run, so tools end and their credential files are deleted, and starts nothing
    /// more. Job states stay as they are, for RecoverAfterRestart to sort out next time.
    /// </summary>
    public async Task StopAllAsync(TimeSpan wait)
    {
        Task[] drives;
        lock (_gate)
        {
            _stopping = true;
            foreach (var run in _running.Values) run.Cancel();
            _running.Clear();
            drives = _drives.Values.ToArray();
        }
        await Task.WhenAny(Task.WhenAll(drives), Task.Delay(wait)).ConfigureAwait(false);
    }

    /// <summary>Call on a timer: releases jobs whose retry wait is over.</summary>
    public void Tick()
    {
        lock (_gate) Pump();
    }

    bool Act(long id, Func<Job, bool> action)
    {
        lock (_gate)
        {
            if (repo.Get(id) is not { } job) return false;
            bool done = action(job);
            if (done) Pump();
            return done;
        }
    }

    bool Move(Job job, JobState to)
    {
        var current = repo.Get(job.Id);
        if (current is null || !JobTransitions.CanMove(current.State, to)) return false;
        var moved = job with { State = to };
        repo.Update(moved);
        JobChanged?.Invoke(this, moved);
        return true;
    }

    void StopRun(long id)
    {
        if (!_running.Remove(id, out var run)) return;
        run.Cancel();
        run.Dispose();
    }

    void Pump()
    {
        if (_stopping) return;
        var now = clock.Now;
        foreach (var job in repo.All().Where(j => j.State == JobState.Waiting))
        {
            if (_retryAt.GetValueOrDefault(job.Id, now) > now) continue;
            _retryAt.Remove(job.Id);
            Move(job, JobState.Queued);
        }

        while (true)
        {
            var jobs = repo.All();
            // A job whose last run is still ending (paused or canceled a moment ago, its tool not yet gone) isn't
            // started again until that run is over: two runs of one job would share its part file (review 2026-11-07).
            // When that run ends, the queue is pumped again.
            var startable = jobs.Where(j => j.State != JobState.Queued || !_drives.ContainsKey(j.Id)).ToList();
            var next = Policy.NextToStart(startable, jobs.Count(j => j.State == JobState.Running));
            if (next is null) return;

            if (next.Kind == JobKind.Captured && vault.Get(next.Id) is null)
            {
                Move(next, JobState.NeedsRecapture);
                continue;
            }

            Move(next, JobState.Running);
            var run = new CancellationTokenSource();
            _running[next.Id] = run;
            long id = next.Id;
            var drive = Drive(repo.Get(id) ?? next, run);
            _drives[id] = drive;
            _ = drive.ContinueWith(done =>
            {
                lock (_gate)
                {
                    if (_drives.GetValueOrDefault(id) != done) return;
                    _drives.Remove(id);
                    Pump();
                }
            }, TaskScheduler.Default);
        }
    }

    async Task Drive(Job job, CancellationTokenSource run)
    {
        JobOutcome outcome;
        try
        {
            outcome = await runner.RunAsync(job, run.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            outcome = JobOutcome.Failure(ErrorKind.ToolFailure);
        }

        lock (_gate)
        {
            // This run is over, so the job may run again. A newer run of it can't exist: Pump starts none while this
            // one's entry is here.
            _drives.Remove(job.Id);
            if (!_running.TryGetValue(job.Id, out var current) || current != run)
            {
                // Paused or canceled while it ran. Its report changes nothing, but a RESUME that came meanwhile can start now.
                Pump();
                return;
            }
            _running.Remove(job.Id);
            run.Dispose();
            Finish(job.Id, outcome);
            Pump();
        }
    }

    void Finish(long id, JobOutcome outcome)
    {
        if (repo.Get(id) is not { State: JobState.Running } job) return;

        if (outcome.IsSuccess)
        {
            vault.Forget(id);
            Move(job, JobState.Completed);
            return;
        }

        var kind = outcome.Error ?? ErrorKind.ToolFailure;
        if (kind == ErrorKind.LinkExpired && job.Kind == JobKind.Captured)
        {
            vault.Forget(id);
            Move(job with { Error = kind }, JobState.NeedsRecapture);
        }
        else if (QueuePolicy.WorthRetrying(kind) && job.Attempts < Policy.Backoff.Length)
        {
            _retryAt[id] = clock.Now + Policy.Backoff[job.Attempts];
            Move(job with { Attempts = job.Attempts + 1, Error = kind }, JobState.Waiting);
        }
        else
        {
            vault.Forget(id);
            Move(job with { Error = kind }, JobState.Failed);
        }
    }
}
