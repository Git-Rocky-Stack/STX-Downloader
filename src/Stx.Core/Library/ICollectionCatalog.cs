using Stx.Core.Queue;

namespace Stx.Core.Library;

/// <summary>A media file found on disk, with the facts a light index keeps. Nothing is read from inside it.</summary>
public sealed record FoundFile(string Path, long Size, DateTimeOffset Modified);

/// <summary>A file the Library already lists, with what was known of it when it was last seen.</summary>
public sealed record IndexedFile(long FileId, long MediaId, string Path, long? Size, DateTimeOffset? Modified, MediaOrigin Origin);

/// <summary>The Library as a whole collection: downloads, files opened in STX, and the watched folders.</summary>
public interface ICollectionCatalog
{
    IReadOnlyList<IndexedFile> IndexedFiles();

    /// <summary>Adds each file as its own item, titled with its file name, in one transaction. A file whose path
    /// (in any case) already has a row isn't added again: the Library has one row per file.</summary>
    void AddLocal(IReadOnlyList<FoundFile> files, MediaOrigin origin);

    /// <summary>A file was renamed or moved. A watched or opened item is retitled with its new name; a download keeps its title.</summary>
    void MoveFile(long fileId, string newPath);

    void UpdateFacts(long fileId, long size, DateTimeOffset modified);

    /// <summary>Takes watched items under the folder out of the Library. Never touches a file on disk, or a download or opened file.</summary>
    int ForgetWatchedUnder(string folder);

    /// <summary>Takes one watched or opened file's row out, and its item when nothing else is left in it. A download's
    /// row is never taken out this way. Never touches the file on disk.</summary>
    void ForgetFile(long fileId);
}
