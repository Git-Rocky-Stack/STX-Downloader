using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Audio;
using Stx.Core.Presets;
using Stx.Core.Queue;

namespace Stx.ViewModels;

public sealed record ConvertAsk(LibraryRowViewModel Row, OutputChoice Output);

public sealed record PresetAsk(LibraryRowViewModel Row, string PresetName);

/// <summary>One saved file. A file that's gone from disk stays listed and says so.</summary>
public sealed class LibraryRowViewModel(LibraryItem item, LibraryFile file, bool onDisk)
{
    public long MediaId { get; } = item.Id;
    public long FileId { get; } = file.Id;
    public string Title { get; } = item.Title ?? item.PageUrl ?? "Untitled";
    public string Path { get; } = file.Path;
    public string Format { get; } = file.Format;
    public IReadOnlyList<string> SiblingFormats { get; } = item.Files.Select(f => f.Format).ToList();
    public bool IsMissing { get; } = !onDisk;
    public bool CanOpen => !IsMissing;
    public bool CanConvert => !IsMissing;

    public string Status => IsMissing
        ? "FILE MISSING"
        : $"{System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant()} · {(Enum.TryParse<OutputChoice>(Format, out var output) && output != OutputChoice.Original ? "CONVERTED" : Format.ToUpperInvariant())}";
}

/// <summary>Everything saved: open it, find it, or convert it. A conversion is its own queued job and keeps the original.</summary>
public sealed class LibraryViewModel : ObservableObject
{
    readonly ILibraryCatalog _catalog;
    readonly QueueEngine _engine;
    readonly IShell _shell;
    readonly Func<string, bool> _exists;
    string _message = "";

    public LibraryViewModel(ILibraryCatalog catalog, QueueEngine engine, IShell shell, Func<string, bool> exists)
    {
        _catalog = catalog;
        _engine = engine;
        _shell = shell;
        _exists = exists;
        OpenCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) _shell.OpenFile(row.Path); });
        RevealCommand = new RelayCommand<LibraryRowViewModel>(row => { if (row is { CanOpen: true }) _shell.RevealFile(row.Path); });
        ConvertCommand = new RelayCommand<ConvertAsk>(ask => { if (ask is not null) Convert(ask.Row, ask.Output, null, ask.Output.ToString()); });
        ConvertPresetCommand = new RelayCommand<PresetAsk>(ConvertPreset);
    }

    public ObservableCollection<LibraryRowViewModel> Rows { get; } = [];
    public IRelayCommand<LibraryRowViewModel> OpenCommand { get; }
    public IRelayCommand<LibraryRowViewModel> RevealCommand { get; }
    public IRelayCommand<ConvertAsk> ConvertCommand { get; }
    public IRelayCommand<PresetAsk> ConvertPresetCommand { get; }

    public bool IsEmpty => Rows.Count == 0;
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public void Refresh()
    {
        Rows.Clear();
        var items = _catalog.Library();
        // Two links can lead to one file. It's listed once, under whichever saved it first.
        var firstRow = items.SelectMany(i => i.Files)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(f => f.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            foreach (var file in item.Files.Where(f => firstRow[f.Path] == f.Id))
                Rows.Add(new LibraryRowViewModel(item, file, _exists(file.Path)));
        OnPropertyChanged(nameof(IsEmpty));
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
