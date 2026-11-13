using Stx.Core.Errors;

namespace Stx.Core.Queue;

public readonly record struct JobOutcome(bool IsSuccess, ErrorKind? Error)
{
    public static JobOutcome Success => new(true, null);
    public static JobOutcome Failure(ErrorKind kind) => new(false, kind);
}

/// <summary>Does the work of one job. Canceling the token must stop any process it started.</summary>
public interface IJobRunner
{
    Task<JobOutcome> RunAsync(Job job, CancellationToken cancel);
}

public interface IJobRepo
{
    Job Add(Job job);
    void Update(Job job);
    Job? Get(long id);
    IReadOnlyList<Job> All();

    /// <summary>Deletes the job's row. Nothing else refers to it: the media it saved stays in the Library.</summary>
    void Remove(long id);
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
