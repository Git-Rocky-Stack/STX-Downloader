using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Audio;
using Stx.Core.Presets;
using Stx.Core.Queue;

namespace Stx.ViewModels;

public sealed record ConvertAsk(LibraryRowViewModel Row, OutputChoice Output);

public sealed record PresetAsk(LibraryRowViewModel Row, string PresetName);

/// <summary>One file in the collection. A file that's gone from disk stays listed and says so.</summary>
public sealed class LibraryRowViewModel(LibraryItem item, LibraryFile file, bool onDisk)
{
    public long MediaId { get; } = item.Id;
    public long FileId { get; } = file.Id;
    public string Title { get; } = item.Title ?? item.PageUrl ?? "Untitled";
    public string Path { get; } = file.Path;
    public string Format { get; } = file.Format;
    /// <summary>Downloaded, opened in STX, or found in a watched folder.</summary>
    public MediaOrigin Origin { get; } = item.Origin;
    public IReadOnlyList<string> SiblingFormats { get; } = item.Files.Select(f => f.Format).ToList();
    public bool IsMissing { get; } = !onDisk;
    public bool CanOpen => !IsMissing;
    public bool CanConvert => !IsMissing;

    public string Status => IsMissing
        ? "FILE MISSING"
        : $"{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()} · {Describe()}";

    // What search looks through: the title, the file's name and the folder it's in.
    readonly string _haystack = $"{item.Title}\n{System.IO.Path.GetFileName(file.Path)}\n{System.IO.Path.GetDirectoryName(file.Path)}".ToLowerInvariant();

    /// <summary>Whether a lower-case search term is in the title, the file name or the folder.</summary>
    public bool Matches(string term) => _haystack.Contains(term, StringComparison.Ordinal);

    /// <summary>Whether the other row shows exactly what this one does, so this one can stay on screen.</summary>
    public bool SameAs(LibraryRowViewModel other) =>
        FileId == other.FileId && MediaId == other.MediaId && Title == other.Title && Path == other.Path && Format == other.Format
        && Origin == other.Origin && IsMissing == other.IsMissing && SiblingFormats.SequenceEqual(other.SiblingFormats);

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

/// <summary>An ObservableCollection that can swap in a whole new list with one Reset, for changes too big to send one by one.</summary>
public sealed class RowCollection : ObservableCollection<LibraryRowViewModel>
{
    public void ReplaceAll(IEnumerable<LibraryRowViewModel> rows)
    {
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// The whole collection: downloads, files opened in STX, and the watched folders. Open it, find it, convert it, or
/// search it. A conversion is its own queued job and keeps the original. Built for tens of thousands of rows: a
/// refresh changes only the rows that changed, and a change too big to send row by row is one reset.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    readonly ILibraryCatalog _catalog;
    readonly QueueEngine _engine;
    readonly IShell _shell;
    readonly Func<string, bool> _exists;
    /// <summary>More changes than this at once and the list is reset instead of edited row by row.</summary>
    const int ResetAbove = 400;
    List<LibraryRowViewModel> _all = [];
    string _message = "", _query = "", _scan = "";

    /// <param name="play">OPEN plays the row on the PLAYER plate. It never hands the file to another app.</param>
    public LibraryViewModel(ILibraryCatalog catalog, QueueEngine engine, IShell shell, Func<string, bool> exists, Action<LibraryRowViewModel> play)
    {
        _catalog = catalog;
        _engine = engine;
        _shell = shell;
        _exists = exists;
        OpenCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) play(row); });
        RevealCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) _shell.RevealFile(row.Path); });
        ConvertCommand = new RelayCommand<ConvertAsk>(ask => { if (ask is not null) Convert(ask.Row, ask.Output, null, ask.Output.ToString()); });
        ConvertPresetCommand = new RelayCommand<PresetAsk>(ConvertPreset);
    }

    /// <summary>The rows shown: all of them, or those the search matches, newest first.</summary>
    public RowCollection Rows { get; } = [];
    public IRelayCommand<LibraryRowViewModel> OpenCommand { get; }
    public IRelayCommand<LibraryRowViewModel> RevealCommand { get; }
    public IRelayCommand<ConvertAsk> ConvertCommand { get; }
    public IRelayCommand<PresetAsk> ConvertPresetCommand { get; }

    /// <summary>Nothing in the Library at all. A search with no matches isn't empty.</summary>
    public bool IsEmpty => _all.Count == 0;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    /// <summary>Search: every word has to be in the title, the file name or the folder. Case doesn't matter.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? "")) return;
            Show();
        }
    }

    /// <summary>How many rows are listed, and of how many when a search is narrowing them.</summary>
    public string CountText => _query.Trim().Length == 0 ? $"{_all.Count:N0} FILES" : $"{Rows.Count:N0} OF {_all.Count:N0}";

    /// <summary>What the watched folders' scan is doing, or empty when it's done.</summary>
    public string ScanText { get => _scan; private set => SetProperty(ref _scan, value); }

    public void ShowScan(Stx.Core.Library.ScanProgress progress) =>
        ScanText = progress.Done ? "" : $"SCANNING {System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(progress.Folder)).ToUpperInvariant()} · {progress.Seen:N0} FOUND";

    /// <summary>Reads the Library again, here and now. Rows that didn't change stay the same objects and aren't redrawn.</summary>
    public void Refresh() => Apply(Build(_all));

    /// <summary>
    /// The same, with the reading done off the UI thread: checking that tens of thousands of files are still on disk
    /// takes too long to do there. Calls that arrive while one runs are folded into one more pass. Call it from the UI thread.
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
            var known = _all;
            var all = await Task.Run(() => Build(known));
            Apply(all);
        }
        while (_again);
    }

    List<LibraryRowViewModel> Build(List<LibraryRowViewModel> known)
    {
        var items = _catalog.Library();
        // Two links can lead to one file. It's listed once, under whichever saved it first.
        var firstRow = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in items.SelectMany(i => i.Files))
            if (!firstRow.TryGetValue(file.Path, out long id) || file.Id < id) firstRow[file.Path] = file.Id;

        var previous = new Dictionary<long, LibraryRowViewModel>(known.Count);
        foreach (var row in known) previous[row.FileId] = row;
        var all = new List<LibraryRowViewModel>(firstRow.Count);
        foreach (var item in items)
            foreach (var file in item.Files)
            {
                if (firstRow[file.Path] != file.Id) continue;
                var row = new LibraryRowViewModel(item, file, _exists(file.Path));
                all.Add(previous.TryGetValue(file.Id, out var old) && old.SameAs(row) ? old : row);
            }
        return all;
    }

    void Apply(List<LibraryRowViewModel> all)
    {
        bool wasEmpty = IsEmpty;
        _all = all;
        Show();
        if (wasEmpty != IsEmpty) OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The row for a path, searched or not. Case doesn't matter.</summary>
    public LibraryRowViewModel? Find(string path) =>
        _all.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    void Show()
    {
        string[] terms = _query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var target = terms.Length == 0 ? _all : _all.Where(r => terms.All(r.Matches)).ToList();
        Sync(target);
        OnPropertyChanged(nameof(CountText));
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
