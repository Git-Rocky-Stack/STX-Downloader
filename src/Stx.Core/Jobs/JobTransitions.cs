namespace Stx.Core.Jobs;

/// <summary>Which state a job may move to next. Completed is final. Anything active can go back in line, so a crash or sleep never leaves a job stuck.</summary>
public static class JobTransitions
{
    static readonly IReadOnlyDictionary<JobState, JobState[]> Allowed = new Dictionary<JobState, JobState[]>
    {
        [JobState.Queued] = [JobState.Running, JobState.Paused, JobState.Canceled],
        [JobState.Running] = [JobState.Queued, JobState.Paused, JobState.Waiting, JobState.Analyzing, JobState.Completed, JobState.Failed, JobState.Canceled, JobState.NeedsRecapture],
        [JobState.Paused] = [JobState.Queued, JobState.Running, JobState.Canceled],
        [JobState.Waiting] = [JobState.Queued, JobState.Running, JobState.Failed, JobState.Canceled],
        [JobState.Analyzing] = [JobState.Queued, JobState.Completed, JobState.Failed, JobState.Canceled],
        [JobState.Completed] = [],
        [JobState.Failed] = [JobState.Queued, JobState.Canceled],
        [JobState.Canceled] = [JobState.Queued],
        [JobState.NeedsRecapture] = [JobState.Queued, JobState.Canceled],
    };

    public static bool CanMove(JobState from, JobState to) => Allowed[from].Contains(to);
}
