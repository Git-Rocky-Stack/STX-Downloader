using Stx.Core.Library;
using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>
/// SHRINK FOLDER (Phase 7): every media file in a folder and its subfolders gets a smaller copy beside it, each its own
/// job in the Queue. Files not in the Library yet join it first, as files opened in STX. Shrink copies, and files that
/// already have one, are skipped. No original is ever changed, moved or deleted.
/// </summary>
public sealed class ShrinkFolder(ICollectionCatalog collection, LibraryViewModel library, Func<string, bool>? ignore = null)
{
    /// <summary>Queues the folder and says, on the Library plate, how many were queued. Returns that number. The folder
    /// and the Library are read off the calling thread; call it from the UI thread, where the queue and the Library live.</summary>
    public async Task<int> QueueAsync(string folder)
    {
        var found = await Task.Run(() => FolderScan.Find(folder, ignore ?? (_ => false)).ToList());
        var unknown = await Task.Run(() => found.Where(f => !library.Lists(f.Path)).ToList());
        if (unknown.Count > 0)
        {
            await Task.Run(() => collection.AddLocal(unknown, MediaOrigin.Opened));
            await library.RefreshAsync();
        }
        var rows = await Task.Run(() => found.Select(f => library.ReadRow(f.Path)).ToList());
        int queued = 0, skipped = 0;
        foreach (var row in rows)
        {
            if (row is not null && library.QueueShrink(row, quiet: true)) queued++;
            else skipped++;
        }
        library.Say(queued == 0
            ? "Nothing in that folder needs a shrink copy."
            : $"Queued {queued} file{(queued == 1 ? "" : "s")} to shrink{(skipped > 0 ? $", skipped {skipped} already done" : "")}. Each copy is checked before it's kept, beside its original.");
        return queued;
    }
}
