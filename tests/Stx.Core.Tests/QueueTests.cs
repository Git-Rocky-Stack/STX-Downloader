using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Xunit;

namespace Stx.Core.Tests;

sealed class FakeRepo : IJobRepo
{
    readonly Dictionary<long, Job> _jobs = new();
    long _next = 1;

    public Job Add(Job job)
    {
        var saved = job with { Id = _next++ };
        _jobs[saved.Id] = saved;
        return saved;
    }

    public void Update(Job job) => _jobs[job.Id] = job;
    public Job? Get(long id) => _jobs.GetValueOrDefault(id);
    public IReadOnlyList<Job> All() => _jobs.Values.OrderBy(j => j.Id).ToList();
    public void Remove(long id) => _jobs.Remove(id);

    /// <summary>Puts a job straight into a state, as the database holds it after a crash.</summary>
    public Job Seed(JobKind kind, JobState state) => Add(new Job(0, 1, kind, state, 0, null, "original", DateTimeOffset.UnixEpoch));
}

sealed class FakeRunner : IJobRunner
{
    public readonly Dictionary<long, TaskCompletionSource<JobOutcome>> Pending = new();
    public readonly Dictionary<long, CancellationToken> Tokens = new();
    public readonly List<long> Started = new();

    public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
    {
        Started.Add(job.Id);
        Tokens[job.Id] = cancel;
        var tcs = new TaskCompletionSource<JobOutcome>();
        Pending[job.Id] = tcs;
        return tcs.Task;
    }

    /// <summary>xUnit's context would defer the engine's continuation to another thread. Without it the run finishes here, so tests read the result directly.</summary>
    public void Finish(long id, JobOutcome outcome)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        Pending[id].SetResult(outcome);
        SynchronizationContext.SetSynchronizationContext(context);
    }

    public void Succeed(long id) => Finish(id, JobOutcome.Success);
    public void Fail(long id, ErrorKind kind) => Finish(id, JobOutcome.Failure(kind));
}

sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

public class QueueEngineTests
{
    readonly FakeRepo _repo = new();
    readonly FakeRunner _runner = new();
    readonly FakeClock _clock = new();
    readonly CaptureVault _vault = new();

    QueueEngine Engine(QueuePolicy? policy = null) => new(_repo, _runner, policy ?? new QueuePolicy(), _clock, _vault);

    static readonly CaptureSecrets Secret = new("https://cdn/x.m3u8?token=abc", new Dictionary<string, string>(), "a=b");

    JobState State(long id) => _repo.Get(id)!.State;

    // Review 2026-11-07: SHRINK pressed twice, or a file and then its folder, queued the same encode twice.
    [Fact]
    public void The_same_conversion_isnt_queued_twice_while_one_is_pending()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        var blocker = engine.Enqueue(9, JobKind.Link, "original");
        var first = engine.Enqueue(1, JobKind.Convert, "shrink-av1");
        var again = engine.Enqueue(1, JobKind.Convert, "shrink-av1");
        Assert.Equal(first.Id, again.Id);
        Assert.True(engine.HasPending(1, JobKind.Convert, "shrink-av1"));
        Assert.NotEqual(first.Id, engine.Enqueue(1, JobKind.Convert, "shrink-hevc").Id);
        Assert.Equal(3, _repo.All().Count);
        _runner.Succeed(blocker.Id);
        _runner.Succeed(first.Id);
        Assert.Equal(JobState.Completed, State(first.Id));
        Assert.False(engine.HasPending(1, JobKind.Convert, "shrink-av1"));
        Assert.NotEqual(first.Id, engine.Enqueue(1, JobKind.Convert, "shrink-av1").Id);
    }

    // Review 2026-11-12: finished rows stacked up in the queue with no way to clear them.
    [Fact]
    public void Only_a_finished_job_can_be_cleared_and_it_leaves_for_good()
    {
        var engine = Engine();
        var removed = new List<long>();
        engine.JobRemoved += (_, id) => removed.Add(id);
        var running = engine.Enqueue(1, JobKind.Link, "original");
        Assert.False(engine.Remove(running.Id));
        _runner.Succeed(running.Id);

        Assert.True(engine.Remove(running.Id));
        Assert.Null(_repo.Get(running.Id));
        Assert.Equal([running.Id], removed);
        Assert.False(engine.Remove(running.Id));
    }

    [Fact]
    public void Clear_done_takes_complete_canceled_and_failed_jobs_and_leaves_the_rest()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        var done = engine.Enqueue(1, JobKind.Link, "original");
        _runner.Succeed(done.Id);
        var failed = engine.Enqueue(2, JobKind.Link, "original");
        _runner.Fail(failed.Id, ErrorKind.DrmDetected);
        var running = engine.Enqueue(3, JobKind.Link, "original");
        var waiting = engine.Enqueue(4, JobKind.Link, "original");
        var paused = engine.Enqueue(5, JobKind.Link, "original");
        Assert.True(engine.Pause(paused.Id));
        var canceled = engine.Enqueue(6, JobKind.Link, "original");
        Assert.True(engine.Cancel(canceled.Id));
        var recapture = _repo.Seed(JobKind.Captured, JobState.NeedsRecapture);

        Assert.Equal(3, engine.RemoveFinished());

        Assert.Equal(new[] { running.Id, waiting.Id, paused.Id, recapture.Id }.Order(), _repo.All().Select(j => j.Id).Order());
        Assert.Equal(0, engine.RemoveFinished());
    }

    // A canceled job's tool may still be ending; its row stays until that run is over, so the run can't report into a
    // row that's gone. Once it ends, the row can go.
    [Fact]
    public void A_canceled_job_whose_tool_is_still_ending_isnt_cleared_yet()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");
        Assert.True(engine.Cancel(job.Id));
        Assert.False(engine.Remove(job.Id));
        _runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.ToolFailure));
        Assert.True(engine.Remove(job.Id));
    }

    [Theory]
    [InlineData(JobState.Completed, true)]
    [InlineData(JobState.Canceled, true)]
    [InlineData(JobState.Failed, true)]
    [InlineData(JobState.Queued, false)]
    [InlineData(JobState.Running, false)]
    [InlineData(JobState.Paused, false)]
    [InlineData(JobState.Waiting, false)]
    [InlineData(JobState.Analyzing, false)]
    [InlineData(JobState.NeedsRecapture, false)]
    public void Finished_means_nothing_more_happens_unless_asked(JobState state, bool finished) =>
        Assert.Equal(finished, QueueEngine.IsFinished(state));

    // Review 2026-11-07: RESUME right after PAUSE started a second run while the first one's tool was still ending.
    [Fact]
    public void A_resumed_job_waits_for_its_last_run_to_end()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");
        Assert.True(engine.Pause(job.Id));
        Assert.True(engine.Resume(job.Id));
        Assert.Equal([job.Id], _runner.Started);
        Assert.Equal(JobState.Queued, State(job.Id));
        _runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.ToolFailure)); // the stopped tool, reporting as it goes
        Assert.Equal(2, _runner.Started.Count);
        Assert.Equal(JobState.Running, State(job.Id));
    }

    [Fact]
    public void Enqueued_job_runs()
    {
        var job = Engine().Enqueue(1, JobKind.Link, "original");
        Assert.Equal(JobState.Running, State(job.Id));
        Assert.Equal([job.Id], _runner.Started);
    }

    [Fact]
    public void Successful_job_completes_and_the_next_one_starts()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        var a = engine.Enqueue(1, JobKind.Link, "original");
        var b = engine.Enqueue(2, JobKind.Link, "original");
        Assert.Equal(JobState.Queued, State(b.Id));

        _runner.Succeed(a.Id);

        Assert.Equal(JobState.Completed, State(a.Id));
        Assert.Equal(JobState.Running, State(b.Id));
    }

    [Fact]
    public void Running_job_at_restart_becomes_Queued()
    {
        var job = _repo.Seed(JobKind.Link, JobState.Running);
        var engine = new QueueEngine(_repo, _runner, new QueuePolicy { MaxParallel = 0 }, _clock, _vault);
        engine.RecoverAfterRestart();
        Assert.Equal(JobState.Queued, State(job.Id));
    }

    [Theory]
    [InlineData(JobState.Running)]
    [InlineData(JobState.Queued)]
    [InlineData(JobState.Paused)]
    [InlineData(JobState.Waiting)]
    [InlineData(JobState.Analyzing)]
    public void Running_captured_job_at_restart_becomes_NeedsRecapture(JobState before)
    {
        // Its stream link and cookies lived only in memory, so a new run has nothing to use.
        var job = _repo.Seed(JobKind.Captured, before);
        Engine().RecoverAfterRestart();
        Assert.Equal(JobState.NeedsRecapture, State(job.Id));
    }

    [Theory]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Canceled)]
    public void Restart_leaves_finished_jobs_alone(JobState state)
    {
        var job = _repo.Seed(JobKind.Captured, state);
        Engine().RecoverAfterRestart();
        Assert.Equal(state, State(job.Id));
    }

    [Fact]
    public void Paused_link_job_stays_paused_through_a_restart()
    {
        var job = _repo.Seed(JobKind.Link, JobState.Paused);
        Engine().RecoverAfterRestart();
        Assert.Equal(JobState.Paused, State(job.Id));
    }

    [Fact]
    public void Nothing_is_stuck_running_after_recovery_with_no_runner_slots()
    {
        _repo.Seed(JobKind.Link, JobState.Running);
        _repo.Seed(JobKind.Link, JobState.Analyzing);
        _repo.Seed(JobKind.Link, JobState.Waiting);
        new QueueEngine(_repo, _runner, new QueuePolicy { MaxParallel = 0 }, _clock, _vault).RecoverAfterRestart();
        Assert.DoesNotContain(_repo.All(), j => j.State is JobState.Running or JobState.Analyzing or JobState.Waiting);
    }

    [Fact]
    public void Captured_jumps_front_and_may_run_one_over_limit()
    {
        var policy = new QueuePolicy { MaxParallel = 3 };
        var link = new Job(1, 1, JobKind.Link, JobState.Queued, 0, null, "original", DateTimeOffset.UnixEpoch);
        var captured = new Job(2, 2, JobKind.Captured, JobState.Queued, 0, null, "original", DateTimeOffset.UnixEpoch);

        Assert.Equal(captured, policy.NextToStart([link, captured], 2));
        Assert.Equal(captured, policy.NextToStart([link, captured], 3));
        Assert.Null(policy.NextToStart([link], 3));
        Assert.Null(policy.NextToStart([captured], 4));
    }

    [Fact]
    public void A_conversion_takes_its_turn_like_a_download()
    {
        var job = Engine().Enqueue(1, JobKind.Convert, "convert:Flac:1");
        Assert.Equal(JobState.Running, State(job.Id));
    }

    [Fact]
    public void A_conversion_survives_a_restart_because_it_needs_no_secrets()
    {
        var job = _repo.Seed(JobKind.Convert, JobState.Running);
        new QueueEngine(_repo, _runner, new QueuePolicy { MaxParallel = 0 }, _clock, _vault).RecoverAfterRestart();
        Assert.Equal(JobState.Queued, State(job.Id));
    }

    [Fact]
    public void Next_to_start_only_considers_queued_jobs()
    {
        var paused = new Job(1, 1, JobKind.Link, JobState.Paused, 0, null, "original", DateTimeOffset.UnixEpoch);
        var failed = new Job(2, 1, JobKind.Captured, JobState.Failed, 0, null, "original", DateTimeOffset.UnixEpoch);
        Assert.Null(new QueuePolicy().NextToStart([paused, failed], 0));
    }

    [Fact]
    public void Oldest_of_a_kind_goes_first()
    {
        var older = new Job(1, 1, JobKind.Link, JobState.Queued, 0, null, "original", DateTimeOffset.UnixEpoch);
        var newer = new Job(2, 1, JobKind.Link, JobState.Queued, 0, null, "original", DateTimeOffset.UnixEpoch);
        Assert.Equal(older, new QueuePolicy().NextToStart([newer, older], 0));
    }

    [Fact]
    public void Engine_lets_a_capture_run_over_the_limit_but_not_a_link()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        engine.Enqueue(1, JobKind.Link, "original");
        var link = engine.Enqueue(2, JobKind.Link, "original");
        var captured = engine.Enqueue(3, JobKind.Captured, "original", Secret);
        Assert.Equal(JobState.Running, State(captured.Id));
        Assert.Equal(JobState.Queued, State(link.Id));
    }

    [Fact]
    public void Fails_after_three_retries_with_backoff()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");

        foreach (double wait in new[] { 5.0, 30.0, 120.0 })
        {
            int starts = _runner.Started.Count;
            _runner.Fail(job.Id, ErrorKind.Network);
            Assert.Equal(JobState.Waiting, State(job.Id));
            Assert.Equal(ErrorKind.Network, _repo.Get(job.Id)!.Error);

            _clock.Advance(wait - 1);
            engine.Tick();
            Assert.Equal(starts, _runner.Started.Count);

            _clock.Advance(1);
            engine.Tick();
            Assert.Equal(starts + 1, _runner.Started.Count);
        }

        _runner.Fail(job.Id, ErrorKind.Network);
        Assert.Equal(JobState.Failed, State(job.Id));
        Assert.Equal(4, _runner.Started.Count);
    }

    [Theory]
    [InlineData(ErrorKind.UnsupportedSite)]
    [InlineData(ErrorKind.SignInNeeded)]
    [InlineData(ErrorKind.PrivateOrUnavailable)]
    [InlineData(ErrorKind.DrmDetected)]
    [InlineData(ErrorKind.OutputFailure)]
    public void Failures_a_retry_cannot_fix_fail_at_once(ErrorKind kind)
    {
        var job = Engine().Enqueue(1, JobKind.Link, "original");
        _runner.Fail(job.Id, kind);
        Assert.Equal(JobState.Failed, State(job.Id));
        Assert.Equal(kind, _repo.Get(job.Id)!.Error);
        Assert.Single(_runner.Started);
    }

    [Fact]
    public void Expired_captured_link_needs_recapture()
    {
        var job = Engine().Enqueue(1, JobKind.Captured, "original", Secret);
        _runner.Fail(job.Id, ErrorKind.LinkExpired);
        Assert.Equal(JobState.NeedsRecapture, State(job.Id));
    }

    [Fact]
    public void Expired_link_job_retries_because_the_tool_fetches_a_fresh_stream()
    {
        var job = Engine().Enqueue(1, JobKind.Link, "original");
        _runner.Fail(job.Id, ErrorKind.LinkExpired);
        Assert.Equal(JobState.Waiting, State(job.Id));
    }

    [Fact]
    public void A_recapture_gives_the_job_new_secrets_and_runs_it()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Captured, "original", Secret);
        _runner.Fail(job.Id, ErrorKind.LinkExpired);
        Assert.Null(_vault.Get(job.Id));

        Assert.True(engine.Recapture(job.Id, Secret));

        Assert.Equal(JobState.Running, State(job.Id));
        Assert.NotNull(_vault.Get(job.Id));
    }

    [Fact]
    public void A_captured_job_with_no_secrets_cannot_start()
    {
        var job = Engine().Enqueue(1, JobKind.Captured, "original");
        Assert.Equal(JobState.NeedsRecapture, State(job.Id));
        Assert.Empty(_runner.Started);
    }

    [Fact]
    public void Pause_stops_process_and_resume_continues()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");

        Assert.True(engine.Pause(job.Id));
        Assert.True(_runner.Tokens[job.Id].IsCancellationRequested);
        Assert.Equal(JobState.Paused, State(job.Id));

        // The stopped process reporting back must not change anything.
        _runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.ToolFailure));
        Assert.Equal(JobState.Paused, State(job.Id));

        Assert.True(engine.Resume(job.Id));
        Assert.Equal(JobState.Running, State(job.Id));
        Assert.Equal(2, _runner.Started.Count);
        Assert.False(_runner.Tokens[job.Id].IsCancellationRequested);
    }

    [Fact]
    public void The_old_process_reporting_late_cannot_touch_the_new_run()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");
        var oldRun = _runner.Pending[job.Id];
        engine.Pause(job.Id);
        engine.Resume(job.Id);
        // Review 2026-11-07: the new run waits for the old one to end, so two tools never share the job's part file.
        Assert.Equal(JobState.Queued, State(job.Id));
        Assert.Single(_runner.Started);

        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        oldRun.SetResult(JobOutcome.Failure(ErrorKind.DrmDetected));
        SynchronizationContext.SetSynchronizationContext(context);

        // Its report changed nothing, and the new run started once it was over.
        Assert.Equal(JobState.Running, State(job.Id));
        Assert.Null(_repo.Get(job.Id)!.Error);
        Assert.Equal(2, _runner.Started.Count);
        var newToken = _runner.Tokens[job.Id];
        Assert.False(newToken.IsCancellationRequested);

        // The new run must still be the one in charge.
        engine.Pause(job.Id);
        Assert.True(newToken.IsCancellationRequested);
    }

    [Fact]
    public void Paused_job_frees_its_slot()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        var a = engine.Enqueue(1, JobKind.Link, "original");
        var b = engine.Enqueue(2, JobKind.Link, "original");
        engine.Pause(a.Id);
        Assert.Equal(JobState.Running, State(b.Id));
    }

    [Fact]
    public void Cancel_is_final_until_retried()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");

        Assert.True(engine.Cancel(job.Id));
        Assert.True(_runner.Tokens[job.Id].IsCancellationRequested);
        Assert.Equal(JobState.Canceled, State(job.Id));

        _runner.Succeed(job.Id);
        engine.Tick();
        Assert.Equal(JobState.Canceled, State(job.Id));
        Assert.False(engine.Resume(job.Id));
        Assert.Equal(JobState.Canceled, State(job.Id));

        Assert.True(engine.Retry(job.Id));
        Assert.Equal(JobState.Running, State(job.Id));
    }

    [Fact]
    public void Retry_starts_the_attempt_count_over()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");
        _runner.Fail(job.Id, ErrorKind.DrmDetected);
        Assert.True(engine.Retry(job.Id));
        Assert.Equal(0, _repo.Get(job.Id)!.Attempts);
        Assert.Null(_repo.Get(job.Id)!.Error);
    }

    [Fact]
    public void A_completed_job_cannot_be_paused_canceled_or_retried()
    {
        var engine = Engine();
        var job = engine.Enqueue(1, JobKind.Link, "original");
        _runner.Succeed(job.Id);
        Assert.False(engine.Pause(job.Id));
        Assert.False(engine.Cancel(job.Id));
        Assert.False(engine.Retry(job.Id));
        Assert.Equal(JobState.Completed, State(job.Id));
    }

    [Fact]
    public void Actions_on_a_job_that_does_not_exist_do_nothing()
    {
        var engine = Engine();
        Assert.False(engine.Pause(99));
        Assert.False(engine.Resume(99));
        Assert.False(engine.Cancel(99));
        Assert.False(engine.Retry(99));
    }

    [Fact]
    public void Parallel_limit_comes_from_settings()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 2 });
        for (int i = 0; i < 4; i++) engine.Enqueue(i, JobKind.Link, "original");
        Assert.Equal(2, _repo.All().Count(j => j.State == JobState.Running));

        engine.Policy = new QueuePolicy { MaxParallel = 3 };
        engine.Tick();
        Assert.Equal(3, _repo.All().Count(j => j.State == JobState.Running));
    }

    [Fact]
    public void Playlist_private_entry_fails_alone_rest_continue()
    {
        var engine = Engine(new QueuePolicy { MaxParallel = 1 });
        var first = engine.Enqueue(1, JobKind.Link, "original");
        var gone = engine.Enqueue(2, JobKind.Link, "original");
        var last = engine.Enqueue(3, JobKind.Link, "original");

        _runner.Succeed(first.Id);
        _runner.Fail(gone.Id, ErrorKind.PrivateOrUnavailable);
        _runner.Succeed(last.Id);

        Assert.Equal(JobState.Completed, State(first.Id));
        Assert.Equal(JobState.Failed, State(gone.Id));
        Assert.Equal(ErrorKind.PrivateOrUnavailable, _repo.Get(gone.Id)!.Error);
        Assert.Equal(JobState.Completed, State(last.Id));
    }

    [Fact]
    public void Secrets_are_forgotten_when_a_job_finishes()
    {
        var job = Engine().Enqueue(1, JobKind.Captured, "original", Secret);
        Assert.NotNull(_vault.Get(job.Id));
        _runner.Succeed(job.Id);
        Assert.Null(_vault.Get(job.Id));
    }

    [Fact]
    public void Secrets_are_forgotten_when_a_job_is_canceled_or_fails()
    {
        var engine = Engine();
        var canceled = engine.Enqueue(1, JobKind.Captured, "original", Secret);
        engine.Cancel(canceled.Id);
        Assert.Null(_vault.Get(canceled.Id));

        var failed = engine.Enqueue(2, JobKind.Captured, "original", Secret);
        _runner.Fail(failed.Id, ErrorKind.DrmDetected);
        Assert.Null(_vault.Get(failed.Id));
    }

    [Fact]
    public void Secrets_never_print()
    {
        string text = Secret.ToString();
        Assert.DoesNotContain("token=abc", text);
        Assert.DoesNotContain("a=b", text);
    }

    [Fact]
    public void A_runner_that_throws_is_a_tool_failure_not_a_stuck_job()
    {
        var repo = new FakeRepo();
        var engine = new QueueEngine(repo, new ThrowingRunner(), new QueuePolicy(), _clock, _vault);
        var job = engine.Enqueue(1, JobKind.Link, "original");
        Assert.Equal(JobState.Waiting, repo.Get(job.Id)!.State);
        Assert.Equal(ErrorKind.ToolFailure, repo.Get(job.Id)!.Error);
    }

    sealed class ThrowingRunner : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Every_change_is_announced()
    {
        var engine = Engine();
        var seen = new List<JobState>();
        engine.JobChanged += (_, job) => seen.Add(job.State);

        var added = engine.Enqueue(1, JobKind.Link, "original");
        _runner.Succeed(added.Id);

        Assert.Equal([JobState.Queued, JobState.Running, JobState.Completed], seen);
    }
}

public class DedupeTests
{
    static readonly MediaFact[] Existing = [new("yt:abc", "hash1", "m4a")];

    [Fact]
    public void Same_source_same_format_is_duplicate() =>
        Assert.Equal(DedupeDecision.Duplicate, Dedupe.Decide("yt:abc", null, "m4a", Existing));

    [Fact]
    public void Same_source_other_format_is_not_duplicate() =>
        Assert.Equal(DedupeDecision.New, Dedupe.Decide("yt:abc", null, "flac", Existing));

    [Fact]
    public void Same_file_from_another_source_is_duplicate() =>
        Assert.Equal(DedupeDecision.Duplicate, Dedupe.Decide("web:xyz", "hash1", "mp4", Existing));

    [Fact]
    public void Different_source_and_no_hash_is_new() =>
        Assert.Equal(DedupeDecision.New, Dedupe.Decide("web:xyz", null, "m4a", Existing));

    [Fact]
    public void Missing_ids_never_match_each_other()
    {
        MediaFact[] blank = [new(null, null, "m4a")];
        Assert.Equal(DedupeDecision.New, Dedupe.Decide(null, null, "m4a", blank));
    }

    [Fact]
    public void Format_comparison_ignores_case() =>
        Assert.Equal(DedupeDecision.Duplicate, Dedupe.Decide("yt:abc", null, "M4A", Existing));
}

public class QueueShutdownTests
{
    [Fact]
    public async Task Quitting_cancels_running_jobs_leaves_them_for_recovery_and_starts_nothing_new()
    {
        var repo = new FakeRepo();
        var runner = new FakeRunner();
        var engine = new QueueEngine(repo, runner, new QueuePolicy { MaxParallel = 1 }, new FakeClock(), new CaptureVault());
        var running = engine.Enqueue(1, JobKind.Link, "x");
        var waiting = engine.Enqueue(1, JobKind.Link, "x");

        await engine.StopAllAsync(TimeSpan.Zero);

        Assert.True(runner.Tokens[running.Id].IsCancellationRequested);
        Assert.Equal(JobState.Running, repo.Get(running.Id)!.State);
        runner.Succeed(running.Id);
        Assert.Equal(JobState.Running, repo.Get(running.Id)!.State);
        Assert.Equal(JobState.Queued, repo.Get(waiting.Id)!.State);
        Assert.DoesNotContain(waiting.Id, runner.Started);

        var late = engine.Enqueue(1, JobKind.Link, "x");
        engine.Tick();
        Assert.DoesNotContain(late.Id, runner.Started);
    }

    [Fact]
    public async Task Nothing_new_starts_once_quitting_even_with_slots_free()
    {
        var runner = new FakeRunner();
        var engine = new QueueEngine(new FakeRepo(), runner, new QueuePolicy { MaxParallel = 3 }, new FakeClock(), new CaptureVault());
        await engine.StopAllAsync(TimeSpan.Zero);
        var late = engine.Enqueue(1, JobKind.Link, "x");
        Assert.DoesNotContain(late.Id, runner.Started);
    }

    [Fact]
    public async Task Quitting_waits_for_a_run_to_clean_up()
    {
        var cleaned = false;
        var engine = new QueueEngine(new FakeRepo(), new CleanupRunner(() => cleaned = true), new QueuePolicy(), new FakeClock(), new CaptureVault());
        engine.Enqueue(1, JobKind.Link, "x");
        await engine.StopAllAsync(TimeSpan.FromSeconds(5));
        Assert.True(cleaned);
    }

    sealed class CleanupRunner(Action cleanup) : IJobRunner
    {
        public async Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
        {
            try { await Task.Delay(Timeout.Infinite, cancel).ConfigureAwait(false); }
            catch (OperationCanceledException) { await Task.Delay(50, CancellationToken.None).ConfigureAwait(false); cleanup(); }
            return JobOutcome.Failure(Stx.Core.Errors.ErrorKind.ToolFailure);
        }
    }
}
