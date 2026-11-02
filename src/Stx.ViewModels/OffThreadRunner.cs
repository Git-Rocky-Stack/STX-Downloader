using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>Creating folders and starting a tool can stall on a slow or unreachable drive, so a job's start never runs on the caller's thread.</summary>
public sealed class OffThreadRunner(IJobRunner inner) : IJobRunner
{
    public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) => Task.Run(() => inner.RunAsync(job, cancel), CancellationToken.None);
}
