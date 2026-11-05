using Stx.Core.Library;
using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>Files opened from outside STX (Explorer, a second launch) join the Library, as downloads do, marked OPENED.</summary>
public sealed class OpenedFiles(ICollectionCatalog collection, LibraryViewModel library)
{
    /// <summary>The Library row for the file: the one already there (the path matched without regard to case), or a new one.</summary>
    public LibraryRowViewModel Adopt(string path)
    {
        if (library.Find(path) is { } existing) return existing;
        var info = new FileInfo(path);
        collection.AddLocal([new FoundFile(path, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.UnixEpoch)], MediaOrigin.Opened);
        library.Refresh();
        return library.Find(path) ?? throw new InvalidOperationException($"{path} was added but isn't in the Library.");
    }
}
