using Stx.Core.Library;
using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>Files opened from outside STX (Explorer, a second launch) join the Library, as downloads do, marked OPENED.</summary>
public sealed class OpenedFiles(ICollectionCatalog collection, LibraryViewModel library)
{
    /// <summary>
    /// The Library row for the file: the one already there (the path matched without regard to case), or a new one.
    /// Call it from the UI thread. Reading the file and writing its row happen off it, and the Library is read again
    /// the way it always is, in the background, so a large Library or a slow drive never freezes the window.
    /// </summary>
    public async Task<LibraryRowViewModel> AdoptAsync(string path)
    {
        if (library.Find(path) is { } existing) return existing;
        await Task.Run(() =>
        {
            var info = new FileInfo(path);
            collection.AddLocal([new FoundFile(path, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTimeOffset.UnixEpoch)], MediaOrigin.Opened);
        });
        await library.RefreshAsync();
        return library.Find(path) ?? throw new InvalidOperationException($"{path} was added but isn't in the Library.");
    }
}
