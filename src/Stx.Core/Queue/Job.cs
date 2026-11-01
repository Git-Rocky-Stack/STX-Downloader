using Stx.Core.Errors;
using Stx.Core.Jobs;

namespace Stx.Core.Queue;

public enum JobKind { Link, Captured }

/// <summary>A job as the database holds it. No stream link, cookie or header is ever a field here.</summary>
public sealed record Job(long Id, long MediaId, JobKind Kind, JobState State, int Attempts, ErrorKind? Error, string Preset, DateTimeOffset CreatedAt);
