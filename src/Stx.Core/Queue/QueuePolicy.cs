using Stx.Core.Errors;
using Stx.Core.Jobs;

namespace Stx.Core.Queue;

/// <summary>Which job runs next and which failures are worth another try.</summary>
public sealed class QueuePolicy
{
    public int MaxParallel { get; init; } = 3;

    public TimeSpan[] Backoff { get; init; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120)];

    /// <summary>A capture goes first and may run one over the limit, because its stream link can expire while it waits.</summary>
    public Job? NextToStart(IReadOnlyList<Job> jobs, int running)
    {
        var queued = jobs.Where(j => j.State == JobState.Queued).OrderBy(j => j.Id).ToList();
        var captured = queued.FirstOrDefault(j => j.Kind == JobKind.Captured);
        if (captured is not null && running <= MaxParallel) return captured;
        return running < MaxParallel ? queued.FirstOrDefault(j => j.Kind != JobKind.Captured) : null;
    }

    public static bool WorthRetrying(ErrorKind kind) => kind is ErrorKind.Network or ErrorKind.ToolFailure or ErrorKind.LinkExpired;
}
