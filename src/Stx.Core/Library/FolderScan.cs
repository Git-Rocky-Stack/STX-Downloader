using Stx.Core.Queue;

namespace Stx.Core.Library;

/// <summary>What a scan of a watched folder changes in the Library.</summary>
public sealed record ScanPlan(IReadOnlyList<FoundFile> Adds, IReadOnlyList<(long FileId, string NewPath)> Moves, IReadOnlyList<(long FileId, long Size, DateTimeOffset Modified)> Updates)
{
    public bool IsEmpty => Adds.Count == 0 && Moves.Count == 0 && Updates.Count == 0;
}

/// <summary>
/// Finds media under a folder and decides what the Library should do about it. The index is light: path, size and
/// date only. Nothing is opened, so a OneDrive file that lives only online isn't downloaded by being listed.
/// </summary>
public static class FolderScan
{
    /// <summary>Every media file under the folder, skipping hidden and system items, folders that link elsewhere
    /// (so a loop of links can't trap the scan), and anything <paramref name="ignore"/> claims.</summary>
    public static IEnumerable<FoundFile> Find(string folder, Func<string, bool> ignore, CancellationToken cancel = default)
    {
        var pending = new Stack<DirectoryInfo>();
        if (Directory.Exists(folder)) pending.Push(new DirectoryInfo(folder));
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            if (ignore(dir.FullName)) continue;
            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }

            foreach (var entry in entries)
            {
                if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (entry is DirectoryInfo sub)
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(sub);
                }
                else if (entry is FileInfo file && MediaTypes.IsMedia(file.Name) && !ignore(file.FullName))
                {
                    FoundFile found;
                    try { found = new FoundFile(file.FullName, file.Length, file.LastWriteTimeUtc); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                    yield return found;
                }
            }
        }
    }

    /// <summary>
    /// New files are added, changed sizes and dates are noted, and a watched file that's gone from its old place is
    /// paired with a new one of the same size and date, so a move or rename while STX was closed keeps its row.
    /// A file already listed under any origin is never added twice.
    /// </summary>
    public static ScanPlan Plan(string folder, IReadOnlyList<FoundFile> found, IReadOnlyList<IndexedFile> indexed, Func<string, bool> exists)
    {
        var byPath = new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in indexed) byPath.TryAdd(file.Path, file);
        var foundPaths = new HashSet<string>(found.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

        var fresh = new List<FoundFile>();
        var updates = new List<(long, long, DateTimeOffset)>();
        foreach (var file in found)
        {
            if (!byPath.TryGetValue(file.Path, out var known)) fresh.Add(file);
            else if (known.Origin != MediaOrigin.Downloaded && (known.Size != file.Size || known.Modified != file.Modified))
                updates.Add((known.FileId, file.Size, file.Modified));
        }

        // A watched file under this folder that isn't where it was. Pair it only when the match is unambiguous both ways.
        var gone = indexed
            .Where(f => f.Origin == MediaOrigin.Watched && f.Size is not null && f.Modified is not null && IsUnder(f.Path, folder) && !foundPaths.Contains(f.Path) && !exists(f.Path))
            .ToList();
        static (long, DateTimeOffset) Key(long size, DateTimeOffset modified) => (size, modified);
        var goneByKey = gone.GroupBy(f => Key(f.Size!.Value, f.Modified!.Value)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var freshByKey = fresh.GroupBy(f => Key(f.Size, f.Modified)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());

        var moves = new List<(long, string)>();
        foreach (var (key, old) in goneByKey)
        {
            if (!freshByKey.TryGetValue(key, out var moved)) continue;
            moves.Add((old.FileId, moved.Path));
            fresh.Remove(moved);
        }
        return new ScanPlan(fresh, moves, updates);
    }

    /// <summary>Whether the path is the folder or inside it. "D:\Music2" isn't inside "D:\Music".</summary>
    public static bool IsUnder(string path, string folder)
    {
        string root = Path.TrimEndingDirectorySeparator(folder);
        if (path.Length < root.Length || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        return path.Length == root.Length || path[root.Length] is '\\' or '/';
    }
}
