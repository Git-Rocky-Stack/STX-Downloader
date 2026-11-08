using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Audio;
using Stx.Core.Logging;
using Stx.Core.Presets;
using Stx.Core.Queue;

namespace Stx.ViewModels;

public sealed record ConvertAsk(LibraryRowViewModel Row, OutputChoice Output);

public sealed record PresetAsk(LibraryRowViewModel Row, string PresetName);

/// <summary>One file in the collection. A file that's gone from disk stays listed and says so.</summary>
public sealed class LibraryRowViewModel(LibraryEntry entry, bool onDisk, long? savedBytes = null, int? savedPercent = null)
{
    /// <summary>For a shrink copy: how many bytes smaller than its original it is (negative if larger), when both are on disk.</summary>
    public long? SavedBytes { get; } = savedBytes;
    public int? SavedPercent { get; } = savedPercent;
    public bool IsShrinkCopy => Format.StartsWith(LibraryEntry.ShrinkPrefix, StringComparison.Ordinal);

    /// <summary>The row's place in the Library's order.</summary>
    public LibraryKey Key { get; } = entry.Key;
    public long MediaId { get; } = entry.MediaId;
    public long FileId { get; } = entry.FileId;
    public string Title { get; } = entry.Name;
    public string Path { get; } = entry.Path;
    public string Format { get; } = entry.Format;
    /// <summary>Downloaded, opened in STX, or found in a watched folder.</summary>
    public MediaOrigin Origin { get; } = entry.Origin;
    public IReadOnlyList<string> SiblingFormats { get; } = entry.SiblingFormats;
    public bool IsMissing { get; } = !onDisk;
    public bool CanOpen => !IsMissing;
    public bool CanConvert => !IsMissing;

    public string Status => IsMissing
        ? "FILE MISSING"
        : $"{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()} · {Describe()}{Saving()}";

    string Saving() => SavedPercent switch
    {
        null => "",
        >= 0 and var p => $" · {p}% SMALLER",
        var p => $" · {-p}% LARGER",
    };

    /// <summary>Whether the other row shows exactly what this one does, so this one can stay on screen.</summary>
    public bool SameAs(LibraryRowViewModel other) =>
        FileId == other.FileId && MediaId == other.MediaId && Title == other.Title && Path == other.Path && Format == other.Format
        && Origin == other.Origin && IsMissing == other.IsMissing && SiblingFormats.SequenceEqual(other.SiblingFormats)
        && SavedBytes == other.SavedBytes && SavedPercent == other.SavedPercent;

    string Describe()
    {
        if (Enum.TryParse<OutputChoice>(Format, out var output) && output != OutputChoice.Original) return "CONVERTED";
        if (Format != "Original") return Format.ToUpperInvariant();
        return Origin switch
        {
            MediaOrigin.Opened => "OPENED",
            MediaOrigin.Watched => "WATCHED",
            _ => "ORIGINAL",
        };
    }
}

/// <summary>
/// The rows read so far, in the Library's order. A list that shows them asks for more as it scrolls
/// (<see cref="LoadMoreRowsAsync"/>); the app's list does it through WinUI's incremental loading. A change too big
/// to send row by row is one reset.
/// </summary>
public class RowCollection : ObservableCollection<LibraryRowViewModel>
{
    internal Func<int, Task<int>>? Loader;
    internal Func<bool>? MoreToLoad;

    /// <summary>Whether the Library (or its search) has rows past the last one here.</summary>
    public bool HasMoreRows => MoreToLoad?.Invoke() ?? false;

    /// <summary>Reads up to <paramref name="count"/> more rows onto the end, off the calling thread. Returns how many came.</summary>
    public Task<int> LoadMoreRowsAsync(int count) => Loader?.Invoke(count) ?? Task.FromResult(0);

    public void ReplaceAll(IEnumerable<LibraryRowViewModel> rows)
    {
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>A file PREV or NEXT plays.</summary>
public sealed record PlayItem(string Path, string Title);

/// <summary>What PREV and NEXT walk: the files before and after the one playing. Null when there's none.</summary>
public interface IPlayOrder
{
    Task<PlayItem?> AfterAsync(string path);
    Task<PlayItem?> BeforeAsync(string path);
}

/// <summary>A fixed list to walk: a stream and its fallbacks, a disc, a file played alone.</summary>
public sealed class ListOrder(IReadOnlyList<(string Path, string Title)> list) : IPlayOrder
{
    int IndexOf(string path) => Enumerable.Range(0, list.Count).FirstOrDefault(i => string.Equals(list[i].Path, path, StringComparison.OrdinalIgnoreCase), -1);

    public Task<PlayItem?> AfterAsync(string path) => At(IndexOf(path) is >= 0 and var i ? i + 1 : -1);

    public Task<PlayItem?> BeforeAsync(string path) => At(IndexOf(path) - 1);

    Task<PlayItem?> At(int index) => Task.FromResult(index >= 0 && index < list.Count ? new PlayItem(list[index].Path, list[index].Title) : null);
}

/// <summary>
/// The whole collection: downloads, files opened in STX, and the watched folders. Open it, find it, convert it, or
/// search it. A conversion is its own queued job and keeps the original. Built for tens of thousands of rows: only the
/// rows read so far are held, a page at a time (the catalog counts, pages and searches the rest), a refresh changes
/// only the rows that changed, and a change too big to send row by row is one reset.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    /// <summary>How many rows are read at once: the first page, and at least this many each time the list asks for more.</summary>
    public const int Page = 100;
    /// <summary>More changes than this at once and the list is reset instead of edited row by row.</summary>
    const int ResetAbove = 400;

    readonly ILibraryCatalog _catalog;
    readonly QueueEngine _engine;
    readonly IShell _shell;
    readonly Func<string, bool> _exists;
    readonly Func<string, long?> _size;
    // One read of the catalog at a time, each applied before the next starts: a refresh, a search, or a page more.
    readonly SemaphoreSlim _reading = new(1, 1);
    // The search the rows answer, and the counts read with them.
    LibrarySearch _search = LibrarySearch.All;
    int _total, _found;
    string _message = "", _query = "", _scan = "";

    /// <param name="play">OPEN plays the row on the PLAYER plate. It never hands the file to another app.</param>
    /// <param name="size">A file's size, for what shrinking saved. Read off the UI thread.</param>
    /// <param name="rows">The collection to fill: the app passes one its list can page through.</param>
    public LibraryViewModel(ILibraryCatalog catalog, QueueEngine engine, IShell shell, Func<string, bool> exists, Action<LibraryRowViewModel> play,
        Func<string, long?>? size = null, RowCollection? rows = null)
    {
        _size = size ?? (path => { try { var info = new FileInfo(path); return info.Exists ? info.Length : null; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; } });
        _catalog = catalog;
        _engine = engine;
        _shell = shell;
        _exists = exists;
        Rows = rows ?? new RowCollection();
        Rows.Loader = LoadMoreAsync;
        Rows.MoreToLoad = () => HasMore;
        OpenCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) play(row); });
        RevealCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) _shell.RevealFile(row.Path); });
        ConvertCommand = new RelayCommand<ConvertAsk>(ask => { if (ask is not null) Convert(ask.Row, ask.Output, null, ask.Output.ToString()); });
        ConvertPresetCommand = new RelayCommand<PresetAsk>(ConvertPreset);
        ShrinkCommand = new RelayCommand<LibraryRowViewModel>(Shrink);
    }

    /// <summary>SHRINK: a smaller copy beside the file, in the video codec chosen on the knob, or Opus for sound alone.</summary>
    public IRelayCommand<LibraryRowViewModel> ShrinkCommand { get; }

    /// <summary>The video codec SHRINK and SHRINK FOLDER use. Sound alone always goes to Opus.</summary>
    public ShrinkCodec ShrinkVideo { get; set; } = ShrinkCodec.Av1;

    /// <summary>What deleting the originals of shrunk files would free. STX never deletes them; this only says how much.</summary>
    public string SpaceText { get => _space; private set => SetProperty(ref _space, value); }
    string _space = "";

    /// <summary>Queues a shrink copy of the row. False, with a plain message, when it can't or needn't be made.</summary>
    public bool QueueShrink(LibraryRowViewModel row, bool quiet = false)
    {
        if (!row.CanConvert) return false;
        if (row.IsShrinkCopy)
        {
            if (!quiet) Message = "That file is already a shrink copy.";
            return false;
        }
        var preset = PresetCatalog.ShrinkFor(Stx.Core.Library.MediaTypes.KindOf(row.Path) != Stx.Core.Library.MediaKind.Audio, ShrinkVideo);
        if (row.SiblingFormats.Contains(preset.Name))
        {
            if (!quiet) Message = "This file already has that shrink copy.";
            return false;
        }
        string plan = new ConversionPlan(preset.Output, row.FileId, preset.Name).ToString();
        if (_engine.HasPending(row.MediaId, JobKind.Convert, plan))
        {
            if (!quiet) Message = "That shrink copy is already in the queue.";
            return false;
        }
        if (!quiet) Message = "";
        _engine.Enqueue(row.MediaId, JobKind.Convert, plan);
        return true;
    }

    void Shrink(LibraryRowViewModel? row)
    {
        if (row is not null) QueueShrink(row);
    }

    /// <summary>Shows a plain line on the Library plate.</summary>
    public void Say(string message) => Message = message;

    /// <summary>The rows read so far, newest first: of the whole Library, or of what the search finds.</summary>
    public RowCollection Rows { get; }
    public IRelayCommand<LibraryRowViewModel> OpenCommand { get; }
    public IRelayCommand<LibraryRowViewModel> RevealCommand { get; }
    public IRelayCommand<ConvertAsk> ConvertCommand { get; }
    public IRelayCommand<PresetAsk> ConvertPresetCommand { get; }

    /// <summary>Nothing in the Library at all. A search with no matches isn't empty.</summary>
    public bool IsEmpty => _total == 0;

    /// <summary>Whether there are rows past the last one read (the list reads them as it scrolls).</summary>
    public bool HasMore => Rows.Count < _found;

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>
    /// Search: every word has to be in the title or the file's path (its name or its folders). Case doesn't matter.
    /// The catalog is searched off the UI thread; the rows change when it answers.
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? "")) return;
            _ = ErrorLog.Observe(RefreshAsync(), "library search");
        }
    }

    /// <summary>Searches, and finishes when the rows show the answer.</summary>
    public Task SearchAsync(string text)
    {
        Query = text;
        return _refreshing ?? Task.CompletedTask;
    }

    /// <summary>How many rows are listed, and of how many when a search is narrowing them.</summary>
    public string CountText => _search.IsAll ? $"{_total:N0} FILES" : $"{_found:N0} OF {_total:N0}";

    /// <summary>What the watched folders' scan is doing, or empty when it's done.</summary>
    public string ScanText { get => _scan; private set => SetProperty(ref _scan, value); }

    public void ShowScan(Stx.Core.Library.ScanProgress progress) =>
        ScanText = progress.Done ? "" : $"SCANNING {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(progress.Folder)).ToUpperInvariant()} · {progress.Seen:N0} FOUND";

    /// <summary>
    /// Reads the Library again here and now, on the calling thread: for a catalog in memory (the gallery's sample,
    /// tests). The app calls <see cref="RefreshAsync"/>. Throws if a read is already under way.
    /// </summary>
    public void Refresh()
    {
        if (!_reading.Wait(0)) throw new InvalidOperationException("The Library is being read already; call RefreshAsync.");
        try
        {
            var ask = Ask();
            Apply(Read(ask));
        }
        finally { _reading.Release(); }
    }

    /// <summary>
    /// Reads the Library again off the UI thread: the counts, the rows on screen (rows that didn't change stay the same
    /// objects and aren't redrawn), and whether their files are still on disk. Calls that arrive while one runs are
    /// folded into one more pass, which also picks up a search typed meanwhile. Call it from the UI thread.
    /// </summary>
    public Task RefreshAsync()
    {
        if (_refreshing is { IsCompleted: false })
        {
            _again = true;
            return _refreshing;
        }
        return _refreshing = RefreshLoop();
    }

    Task? _refreshing;
    bool _again;

    async Task RefreshLoop()
    {
        do
        {
            _again = false;
            await _reading.WaitAsync();
            try
            {
                var ask = Ask();
                var read = await Task.Run(() => Read(ask));
                // Typed on while it read: that answer is for an older search, so read again for this one.
                if (!ask.Search.Equals(LibrarySearch.Parse(_query))) _again = true;
                else Apply(read);
            }
            finally { _reading.Release(); }
        }
        while (_again);
    }

    // What a pass reads, taken on the UI thread: the search typed, and for the same search as on screen, how far the
    // rows go, so the pass keeps them.
    sealed record Asking(LibrarySearch Search, int Loaded, LibraryKey? Last, bool Complete, Dictionary<long, LibraryRowViewModel> Known);

    sealed record Answer(LibrarySearch Search, List<LibraryRowViewModel> Rows, int Total, int Found, string Space);

    Asking Ask()
    {
        var search = LibrarySearch.Parse(_query);
        var known = new Dictionary<long, LibraryRowViewModel>(Rows.Count);
        foreach (var row in Rows) known[row.FileId] = row;
        if (!search.Equals(_search)) return new Asking(search, 0, null, true, known);
        return new Asking(search, Rows.Count, Rows.Count > 0 ? Rows[^1].Key : null, !HasMore, known);
    }

    // Off the UI thread. A new search, or an empty list, reads the first page. Otherwise everything from the top down
    // to the last row on screen is read again: rows that arrived above it are inserted, rows gone are removed, and the
    // list still ends where it ended. When every row was on screen, it reads to the end instead, so a file added below
    // the last row (a copy saved beside its original) shows. More than ResetAbove new rows is a big change: the list
    // starts again from the top, as long as it was.
    Answer Read(Asking ask)
    {
        int total = _catalog.Count(LibrarySearch.All);
        int found = ask.Search.IsAll ? total : _catalog.Count(ask.Search);
        int limit = ask.Loaded == 0 ? Page : ask.Loaded + ResetAbove + 1;
        var fetched = _catalog.Rows(ask.Search, null, limit);
        int take;
        if (ask.Loaded == 0) take = fetched.Count;
        else if (ask.Complete || ask.Last is not { } last) take = fetched.Count < limit ? fetched.Count : Math.Max(Page, ask.Loaded);
        else
        {
            int through = fetched.TakeWhile(e => e.Key <= last).Count();
            take = through < fetched.Count || fetched.Count < limit ? through : Math.Max(Page, ask.Loaded);
        }
        var rows = new List<LibraryRowViewModel>(take);
        foreach (var entry in fetched.Take(take))
        {
            var row = Make(entry);
            rows.Add(ask.Known.TryGetValue(entry.FileId, out var old) && old.SameAs(row) ? old : row);
        }
        return new Answer(ask.Search, rows, total, found, Space());
    }

    // A row as it is now: whether its file is on disk and, for a shrink copy, what it saved against its original.
    LibraryRowViewModel Make(LibraryEntry entry)
    {
        long? saved = null;
        int? percent = null;
        if (entry is { IsShrinkCopy: true, OriginalPath: { } original } && _size(original) is { } before && before > 0 && _size(entry.Path) is { } copy)
        {
            saved = before - copy;
            percent = (int)Math.Round(saved.Value * 100.0 / before);
        }
        return new LibraryRowViewModel(entry, _exists(entry.Path), saved, percent);
    }

    // One original per item, however many copies it has, measured against its smallest copy.
    string Space()
    {
        var freed = new Dictionary<long, long>();
        foreach (var pair in _catalog.ShrinkCopies())
        {
            if (_size(pair.OriginalPath) is not { } before || before <= 0 || _size(pair.CopyPath) is not { } copy) continue;
            long saved = before - copy;
            if (saved > 0 && (!freed.TryGetValue(pair.MediaId, out long most) || saved > most)) freed[pair.MediaId] = saved;
        }
        return freed.Count == 0 ? ""
            : $"DELETING {freed.Count} ORIGINAL{(freed.Count == 1 ? "" : "S")} WOULD FREE {Bytes(freed.Values.Sum())}. STX NEVER DELETES THEM.";
    }

    void Apply(Answer answer)
    {
        bool wasEmpty = IsEmpty, hadMore = HasMore;
        _search = answer.Search;
        _total = answer.Total;
        _found = answer.Found;
        SpaceText = answer.Space;
        Sync(answer.Rows);
        OnPropertyChanged(nameof(CountText));
        if (wasEmpty != IsEmpty) OnPropertyChanged(nameof(IsEmpty));
        if (hadMore != HasMore) OnPropertyChanged(nameof(HasMore));
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> more rows (at least a page) after the last one, off the UI thread, and adds
    /// them to the end. Returns how many came: none once the Library, or its search, has no more.
    /// </summary>
    public async Task<int> LoadMoreAsync(int count)
    {
        await _reading.WaitAsync();
        try
        {
            if (!HasMore) return 0;
            var search = _search;
            LibraryKey? after = Rows.Count > 0 ? Rows[^1].Key : null;
            int want = Math.Max(count, Page);
            var rows = await Task.Run(() => _catalog.Rows(search, after, want).Select(Make).ToList());
            foreach (var row in rows) Rows.Add(row);
            // Fewer than asked for means the end: rows counted earlier have gone since. Nothing more is asked for.
            if (rows.Count < want) _found = Rows.Count;
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(HasMore));
            return rows.Count;
        }
        finally { _reading.Release(); }
    }

    static string Bytes(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

    /// <summary>Whether the Library lists a file at the path, searched or not. Case doesn't matter. Safe off the UI thread.</summary>
    public bool Lists(string path) => _catalog.Row(path) is not null;

    /// <summary>
    /// The row for a path, searched or not, read now: whether its file is on disk is checked, so call it off the UI
    /// thread. Case doesn't matter. Null when the Library doesn't list the file.
    /// </summary>
    public LibraryRowViewModel? ReadRow(string path) => _catalog.Row(path) is { } entry ? Make(entry) : null;

    /// <summary>The row for a path, read off the UI thread; the row on screen when that one is showing. Call it from the UI thread.</summary>
    public async Task<LibraryRowViewModel?> FindAsync(string path)
    {
        var row = await Task.Run(() => ReadRow(path));
        return row is null ? null : Rows.FirstOrDefault(r => r.SameAs(row)) ?? row;
    }

    /// <summary>
    /// What PREV and NEXT walk from a row played now: the Library in its order, as the search on screen narrows it,
    /// skipping files gone from disk. It's read from the catalog when a step is taken, off the UI thread.
    /// </summary>
    public IPlayOrder PlayOrder() => new LibraryOrder(this, _search);

    sealed class LibraryOrder(LibraryViewModel library, LibrarySearch search) : IPlayOrder
    {
        public Task<PlayItem?> AfterAsync(string path) => Task.Run(() => library.Step(search, path, forward: true));
        public Task<PlayItem?> BeforeAsync(string path) => Task.Run(() => library.Step(search, path, forward: false));
    }

    PlayItem? Step(LibrarySearch search, string path, bool forward)
    {
        if (_catalog.Row(path) is not { } at) return null;
        var from = at.Key;
        while (true)
        {
            var page = forward ? _catalog.Rows(search, from, Page) : _catalog.RowsBefore(search, from, Page);
            foreach (var entry in page)
                if (_exists(entry.Path)) return new PlayItem(entry.Path, entry.Name);
            if (page.Count < Page) return null;
            from = page[^1].Key;
        }
    }

    // Both lists keep the Library's order, so what's on screen is always edited into place, never shuffled.
    void Sync(List<LibraryRowViewModel> target)
    {
        var wanted = new HashSet<LibraryRowViewModel>(target, ReferenceEqualityComparer.Instance);
        int removals = Rows.Count(r => !wanted.Contains(r));
        int inserts = target.Count - (Rows.Count - removals);
        if (removals + inserts > ResetAbove)
        {
            Rows.ReplaceAll(target);
            return;
        }
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Rows[i])) Rows.RemoveAt(i);
        for (int i = 0; i < target.Count; i++)
            if (i >= Rows.Count || !ReferenceEquals(Rows[i], target[i])) Rows.Insert(i, target[i]);
    }

    void ConvertPreset(PresetAsk? ask)
    {
        if (ask is null || PresetCatalog.All.FirstOrDefault(p => p.Name == ask.PresetName) is not { } preset) return;
        Convert(ask.Row, preset.Output, preset.Name, preset.Name);
    }

    void Convert(LibraryRowViewModel row, OutputChoice output, string? presetName, string recordedAs)
    {
        Message = "";
        if (!row.CanConvert) return;
        if (row.SiblingFormats.Contains(recordedAs))
        {
            Message = "This item is already saved in that format.";
            return;
        }
        _engine.Enqueue(row.MediaId, JobKind.Convert, new ConversionPlan(output, row.FileId, presetName).ToString());
    }
}
