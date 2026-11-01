namespace Stx.Core.Queue;

public sealed record MediaRecord(long Id, string? PageUrl, string? SourceId, string? Title);

/// <summary>What's been saved, so the queue can name jobs and skip duplicates.</summary>
public interface IMediaCatalog
{
    long AddItem(string? pageUrl, string? sourceId, string? title);
    MediaRecord? GetItem(long id);
    long AddFile(long mediaId, string path, string? hash, string format);
    IReadOnlyList<MediaFact> ExistingFacts();
}
