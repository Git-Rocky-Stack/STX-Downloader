namespace Stx.Core.Queue;

public sealed record MediaRecord(long Id, string? PageUrl, string? SourceId, string? Title);

/// <summary>What's been saved, so the queue can name jobs and skip duplicates.</summary>
public interface IMediaCatalog
{
    long AddItem(string? pageUrl, string? sourceId, string? title);
    MediaRecord? GetItem(long id);
    long AddFile(long mediaId, string path, string? hash, string format);
    IReadOnlyList<MediaFact> ExistingFacts();
    void RememberCapture(string key, long mediaId);
    long? RememberedCapture(string key);
}

public sealed record LibraryFile(long Id, string Path, string Format);

public sealed record LibraryItem(long Id, string? Title, string? PageUrl, IReadOnlyList<LibraryFile> Files);

public interface ILibraryCatalog
{
    IReadOnlyList<LibraryItem> Library();
}

/// <summary>What was measured from a file, kept so selecting it later needn't analyze it again.</summary>
public interface IFactsStore
{
    void SaveFacts(long fileId, string json);
    string? LoadFacts(long fileId);
}
