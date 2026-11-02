using System.Collections.Concurrent;

namespace Stx.Core.Queue;

/// <summary>What a captured job needs to fetch its stream. Held in memory only, and it refuses to print itself.</summary>
public sealed record CaptureSecrets(string StreamUrl, IReadOnlyDictionary<string, string> Headers, string? Cookies)
{
    public override string ToString() => "CaptureSecrets([redacted])";
}

/// <summary>Captured job secrets, by job id. Nothing here reaches the disk, so a restart forgets them by design.</summary>
public sealed class CaptureVault
{
    readonly ConcurrentDictionary<long, CaptureSecrets> _secrets = new();

    public void Put(long jobId, CaptureSecrets secrets) => _secrets[jobId] = secrets;
    public CaptureSecrets? Get(long jobId) => _secrets.GetValueOrDefault(jobId);
    public void Forget(long jobId) => _secrets.TryRemove(jobId, out _);
}
