namespace Stx.Core.Queue;

public enum DedupeDecision { New, Duplicate }

public sealed record MediaFact(string? SourceId, string? Hash, string Format, string? Path = null, long MediaId = 0, long FileId = 0);

/// <summary>A second format of the same source is wanted. The same source in the same format, or the same bytes from anywhere, is not.</summary>
public static class Dedupe
{
    public static DedupeDecision Decide(string? sourceId, string? hash, string format, IEnumerable<MediaFact> existing)
    {
        foreach (var fact in existing)
        {
            bool sameSource = !string.IsNullOrEmpty(sourceId) && fact.SourceId == sourceId && string.Equals(fact.Format, format, StringComparison.OrdinalIgnoreCase);
            bool sameBytes = !string.IsNullOrEmpty(hash) && fact.Hash == hash;
            if (sameSource || sameBytes) return DedupeDecision.Duplicate;
        }
        return DedupeDecision.New;
    }

    /// <summary>Another saved file with these exact bytes. A file is never its own duplicate.</summary>
    public static MediaFact? SameBytes(string hash, string path, IEnumerable<MediaFact> existing) =>
        existing.FirstOrDefault(f => f.Hash == hash && f.Path is not null && !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
}
