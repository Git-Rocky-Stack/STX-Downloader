using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>Files opened from outside STX (Explorer, a second launch) join the Library, as downloads do.</summary>
public sealed class OpenedFiles(IMediaCatalog media, LibraryViewModel library)
{
    /// <summary>The Library row for the file: the one already there (the path matched without regard to case), or a new one.</summary>
    public LibraryRowViewModel Adopt(string path)
    {
        if (Find(path) is { } existing) return existing;
        long id = media.AddItem(null, null, Path.GetFileNameWithoutExtension(path));
        media.AddFile(id, path, null, SourceViewModel.OriginalFormat);
        library.Refresh();
        return Find(path) ?? throw new InvalidOperationException($"{path} was added but isn't in the Library.");
    }

    LibraryRowViewModel? Find(string path) =>
        library.Rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
}
