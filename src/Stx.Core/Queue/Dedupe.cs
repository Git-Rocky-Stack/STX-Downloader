namespace Stx.Core.Queue;

public enum DedupeDecision { New, Duplicate }

public sealed record MediaFact(string? SourceId, string? Hash, string Format);

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
}
