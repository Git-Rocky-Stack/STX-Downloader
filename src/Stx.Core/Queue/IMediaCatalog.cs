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

/// <summary>How an item came into the Library. Each row says which.</summary>
public enum MediaOrigin { Downloaded, Opened, Watched }

public sealed record LibraryItem(long Id, string? Title, string? PageUrl, IReadOnlyList<LibraryFile> Files, MediaOrigin Origin = MediaOrigin.Downloaded);

/// <summary>
/// The Library, read a page at a time. Its order: the newest item first, and an item's files in the order they were
/// saved. A file is listed once, under the first row saved for its path (two links can lead to one file). Search
/// looks in each row's title and path. Nothing here holds the whole Library in memory.
/// </summary>
public interface ILibraryCatalog
{
    /// <summary>How many listed rows the search finds (every row, for <see cref="LibrarySearch.All"/>).</summary>
    int Count(LibrarySearch search);

    /// <summary>Up to <paramref name="limit"/> rows the search finds, in the Library's order, from just after <paramref name="after"/> (or the top).</summary>
    IReadOnlyList<LibraryEntry> Rows(LibrarySearch search, LibraryKey? after, int limit);

    /// <summary>Up to <paramref name="limit"/> rows the search finds that come before <paramref name="before"/>, nearest first.</summary>
    IReadOnlyList<LibraryEntry> RowsBefore(LibrarySearch search, LibraryKey before, int limit);

    /// <summary>The listed row for a path, whatever the search. Case doesn't matter.</summary>
    LibraryEntry? Row(string path);

    /// <summary>Every listed shrink copy, with its item's original, for what deleting originals would free.</summary>
    IReadOnlyList<ShrinkPair> ShrinkCopies();

    /// <summary>
    /// Takes files off the Library (REMOVE, CLEAR MISSING): every row for each path, in any case, so no second link to
    /// the same file takes its place, and each item left with no file at all. Nothing on disk is touched. A download
    /// removed this way can be saved again. Returns how many of the paths were listed.
    /// </summary>
    int Forget(IReadOnlyCollection<string> paths);
}

/// <summary>
/// A row's place in the Library's order. Earlier means higher in the list: a newer item, or within one item, a file
/// saved before.
/// </summary>
public readonly record struct LibraryKey(long MediaId, long FileId) : IComparable<LibraryKey>
{
    public int CompareTo(LibraryKey other) => MediaId != other.MediaId ? other.MediaId.CompareTo(MediaId) : FileId.CompareTo(other.FileId);

    public static bool operator <(LibraryKey a, LibraryKey b) => a.CompareTo(b) < 0;
    public static bool operator >(LibraryKey a, LibraryKey b) => a.CompareTo(b) > 0;
    public static bool operator <=(LibraryKey a, LibraryKey b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LibraryKey a, LibraryKey b) => a.CompareTo(b) >= 0;
}

/// <summary>One listed row: a file, with its item's facts. <see cref="OriginalPath"/> is set for a shrink copy, to measure it against.</summary>
public sealed record LibraryEntry(long MediaId, long FileId, string? Title, string? PageUrl, MediaOrigin Origin, string Path, string Format,
    IReadOnlyList<string> SiblingFormats, string? OriginalPath = null)
{
    /// <summary>What a shrink copy's format starts with ("Shrink AV1", "Shrink HEVC", "Shrink Opus").</summary>
    public const string ShrinkPrefix = "Shrink ";

    public LibraryKey Key => new(MediaId, FileId);

    /// <summary>What the row is called: its title, or its link, or "Untitled".</summary>
    public string Name => Title ?? PageUrl ?? "Untitled";

    public bool IsShrinkCopy => Format.StartsWith(ShrinkPrefix, StringComparison.Ordinal);
}

/// <summary>
/// Text with every letter's case folded, accented and non-Latin letters too: how two spellings of a path are known
/// to be one file, as Windows folds them, and how a search word is found in a title or a path. It's the rule of
/// <see cref="StringComparison.OrdinalIgnoreCase"/>, which compares text folded this way, so the database (which
/// keeps these keys) and the in-memory catalog (which compares with it) give the same answers.
/// </summary>
public static class CaseKey
{
    public static string? Of(string? text) => text?.ToUpperInvariant();
}

/// <summary>A listed shrink copy and the original it was made from.</summary>
public sealed record ShrinkPair(long MediaId, string OriginalPath, string CopyPath);

/// <summary>
/// A Library search: every word has to be in the row's title or its file's path (its name or its folders), at any
/// length. Case doesn't matter (<see cref="CaseKey"/>). No words finds everything.
/// </summary>
public sealed class LibrarySearch : IEquatable<LibrarySearch>
{
    public static readonly LibrarySearch All = new([]);

    LibrarySearch(IReadOnlyList<string> terms) => Terms = terms;

    public IReadOnlyList<string> Terms { get; }

    public bool IsAll => Terms.Count == 0;

    public static LibrarySearch Parse(string? text)
    {
        string[] terms = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return terms.Length == 0 ? All : new LibrarySearch(terms);
    }

    /// <summary>Whether a word is in a title or a path: the rule every catalog keeps.</summary>
    public static bool Has(string? text, string term) => text is not null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    public bool Matches(string? title, string path) => Terms.All(t => Has(title, t) || Has(path, t));

    public bool Equals(LibrarySearch? other) => other is not null && Terms.SequenceEqual(other.Terms, StringComparer.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as LibrarySearch);

    public override int GetHashCode() => Terms.Aggregate(0, (hash, t) => HashCode.Combine(hash, StringComparer.OrdinalIgnoreCase.GetHashCode(t)));

    public override string ToString() => string.Join(' ', Terms);
}

/// <summary>
/// A catalog kept in memory, for the gallery's sample and for tests: the Library's rules applied to a list of items.
/// The database answers the same questions with queries (Stx.Data.MediaRepo), and a test holds the two to the same
/// answers, so this is also the rules written down.
/// </summary>
public abstract class InMemoryLibrary : ILibraryCatalog
{
    /// <summary>Every item, newest first, each with its files in the order they were saved.</summary>
    protected abstract IReadOnlyList<LibraryItem> AllItems();

    // Paths taken off the Library. A file under one of them is no longer an item's file; an item left with none is gone.
    readonly HashSet<string> _forgotten = new(StringComparer.OrdinalIgnoreCase);

    public int Forget(IReadOnlyCollection<string> paths)
    {
        lock (_forgotten)
        {
            var listed = Listed().Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int count = paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(listed.Contains);
            _forgotten.UnionWith(paths);
            return count;
        }
    }

    IReadOnlyList<LibraryItem> Kept()
    {
        lock (_forgotten)
        {
            var items = AllItems();
            if (_forgotten.Count == 0) return items;
            return [.. items.Select(i => i with { Files = [.. i.Files.Where(f => !_forgotten.Contains(f.Path))] }).Where(i => i.Files.Count > 0)];
        }
    }

    IEnumerable<LibraryEntry> Listed()
    {
        var items = Kept();
        var first = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in items.SelectMany(i => i.Files))
            if (!first.TryGetValue(file.Path, out long id) || file.Id < id) first[file.Path] = file.Id;
        foreach (var item in items)
        {
            var formats = item.Files.Select(f => f.Format).ToList();
            string? original = item.Files.FirstOrDefault(f => f.Format == "Original")?.Path;
            foreach (var file in item.Files)
            {
                if (first[file.Path] != file.Id) continue;
                bool copy = file.Format.StartsWith(LibraryEntry.ShrinkPrefix, StringComparison.Ordinal);
                yield return new LibraryEntry(item.Id, file.Id, item.Title, item.PageUrl, item.Origin, file.Path, file.Format, formats, copy ? original : null);
            }
        }
    }

    IEnumerable<LibraryEntry> Found(LibrarySearch search) => Listed().Where(e => search.Matches(e.Title, e.Path));

    public int Count(LibrarySearch search) => Found(search).Count();

    public IReadOnlyList<LibraryEntry> Rows(LibrarySearch search, LibraryKey? after, int limit) =>
        Found(search).Where(e => after is not { } a || e.Key > a).Take(limit).ToList();

    public IReadOnlyList<LibraryEntry> RowsBefore(LibrarySearch search, LibraryKey before, int limit) =>
        Found(search).Where(e => e.Key < before).Reverse().Take(limit).ToList();

    public LibraryEntry? Row(string path) => Listed().FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ShrinkPair> ShrinkCopies() =>
        Listed().Where(e => e is { IsShrinkCopy: true, OriginalPath: not null }).Select(e => new ShrinkPair(e.MediaId, e.OriginalPath!, e.Path)).ToList();
}

/// <summary>What was measured from a file, kept so selecting it later needn't analyze it again.</summary>
public interface IFactsStore
{
    void SaveFacts(long fileId, string json);
    string? LoadFacts(long fileId);
}
