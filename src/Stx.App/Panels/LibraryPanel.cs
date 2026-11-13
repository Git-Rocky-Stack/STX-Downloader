using System.ComponentModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>
/// The whole collection, as a native list that only builds the rows on screen, so tens of thousands of files scroll
/// and search without a wait. Selecting a file shows its truth in the panel beside it.
/// </summary>
public sealed partial class LibraryPanel : ContentControl
{
    // A row's look is built in code; the template only gives each recycled row a place to put it.
    const string RowTemplate =
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
        "<ContentControl HorizontalContentAlignment=\"Stretch\" HorizontalAlignment=\"Stretch\" IsTabStop=\"False\"/></DataTemplate>";

    readonly LibraryViewModel _library;
    readonly Action<LibraryRowViewModel?> _selected;
    readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        ItemTemplate = (DataTemplate)XamlReader.Load(RowTemplate),
    };
    readonly StateView _empty = new() { State = ViewState.Empty, Message = "NOTHING SAVED YET", VerticalAlignment = VerticalAlignment.Center };
    readonly DisplayTextBox _search = new() { Label = "Search the library", PlaceholderText = "SEARCH TITLE, FILE OR FOLDER…", Tone = DisplayTone.Cyan };
    readonly GlowText _count = new() { Tone = DisplayTone.Cyan, Dimmed = true, TextSize = 13 };
    readonly Pad _open = new() { Content = "OPEN" }, _reveal = new() { Content = "FIND FILE" }, _car = new() { Content = "CAR USB" }, _phone = new() { Content = "PHONE" }, _archive = new() { Content = "ARCHIVE" }, _shrink = new() { Content = "SHRINK" };
    // Housekeeping: rows leave the Library; files never leave the disk.
    readonly Pad _remove = new() { Content = "REMOVE" }, _clearMissing = new() { Content = "CLEAR MISSING" };
    readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Visibility = Visibility.Collapsed };
    long? _selectedFile;

    /// <param name="shrinkFolder">SHRINK FOLDER: picks a folder and queues a shrink copy of everything in it.</param>
    public LibraryPanel(LibraryViewModel library, Action<LibraryRowViewModel?> selected, Func<Task>? shrinkFolder = null)
    {
        _library = library;
        _selected = selected;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_list, "Library");
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            _list.Resources[$"ListViewItemSelectionIndicator{state}Brush"] = LookBrushes.From("#58C7FF");
        _list.ItemsSource = library.Rows;
        _list.ContainerContentChanging += OnContainer;

        _list.SelectionChanged += (_, _) =>
        {
            if (Selected is { } row) _selectedFile = row.FileId;
            else if (_list.Items.Count > 0 && library.Rows.All(r => r.FileId != _selectedFile)) _selectedFile = null;
            PaintActions();
            _selected(Selected);
        };
        _search.TextChanged += (_, _) => library.Query = _search.Text;
        _open.Click += (_, _) => library.OpenCommand.Execute(Selected);
        _reveal.Click += (_, _) => library.RevealCommand.Execute(Selected);
        _car.Click += (_, _) => Convert("Car USB");
        _phone.Click += (_, _) => Convert("Phone");
        _archive.Click += (_, _) => Convert("Archive");
        _remove.Click += (_, _) => library.RemoveCommand.Execute(Selected);
        _clearMissing.Click += (_, _) => library.ClearMissingCommand.Execute(null);
        AutomationProperties.SetHelpText(_remove, "Takes the selected file off the Library. The file on disk isn't touched.");
        AutomationProperties.SetHelpText(_clearMissing, "Takes every file that's gone from disk off the Library.");

        var actions = new PadRow();
        foreach (var pad in new[] { _open, _reveal, _car, _phone, _archive, _remove, _clearMissing }) actions.Children.Add(pad);
        TourTargets.Mark(this, "library");
        TourTargets.Mark(_open, "library.open");
        TourTargets.Mark(_car, "library.convert");
        TourTargets.Mark(_remove, "library.remove");
        TourTargets.Mark(_clearMissing, "library.clearmissing");
        TourTargets.Mark(_shrink, "library.shrink");
        TourTargets.Mark(_count, "library.count");

        // Shrink (Phase 7): a smaller copy beside the original, in the codec on the knob; sound alone goes to Opus.
        var codec = new Rotary { Label = "Shrink to", Items = ["AV1", "HEVC"] };
        codec.SelectedIndex = library.ShrinkVideo == Stx.Core.Presets.ShrinkCodec.Hevc ? 1 : 0;
        codec.SelectionChanged += (_, index) => library.ShrinkVideo = index == 1 ? Stx.Core.Presets.ShrinkCodec.Hevc : Stx.Core.Presets.ShrinkCodec.Av1;
        _shrink.Click += (_, _) => { if (Selected is { } row) library.ShrinkCommand.Execute(row); };
        var shrinkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var dial = codec.Captioned();
        dial.VerticalAlignment = VerticalAlignment.Center;
        shrinkRow.Children.Add(dial);
        _shrink.VerticalAlignment = VerticalAlignment.Center;
        shrinkRow.Children.Add(_shrink);
        if (shrinkFolder is not null)
        {
            var folder = TourTargets.Mark(new Pad { Content = "SHRINK FOLDER", VerticalAlignment = VerticalAlignment.Center }, "library.shrinkfolder");
            folder.Click += async (_, _) => await shrinkFolder();
            shrinkRow.Children.Add(folder);
        }

        // Three bands: the search on top, the list in the middle, the pads at the bottom. The middle band takes whatever
        // height the deck gives the plate (DeckPanel), so the plate meets the Source truth plate's bottom edge with no
        // dead space, and the list shows more rows instead of empty metal.
        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Library" });
        top.Children.Add(TourTargets.Mark(new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(6), Content = _search }, "library.search"));
        top.Children.Add(_count);
        var rows = new Grid { MinHeight = 72 };
        rows.Children.Add(_empty);
        rows.Children.Add(new ListWell { Children = { _list } });
        var middle = TourTargets.Mark(ListTray.Around(rows), "library.list");
        var bottom = new StackPanel { Spacing = 12 };
        bottom.Children.Add(_message);
        bottom.Children.Add(actions);
        bottom.Children.Add(shrinkRow);
        var column = new Grid { RowSpacing = 12 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(middle, 1);
        Grid.SetRow(bottom, 2);
        column.Children.Add(top);
        column.Children.Add(middle);
        column.Children.Add(bottom);
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = new Faceplate { PanelName = "library", Padding = new Thickness(36, 28, 36, 28), Content = column };

        library.Rows.CollectionChanged += OnRows;
        library.PropertyChanged += OnLibraryChanged;
        PaintActions();
        PaintCount();
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
    }

    LibraryRowViewModel? Selected => _list.SelectedItem as LibraryRowViewModel;

    void Convert(string preset)
    {
        if (Selected is { } row) _library.ConvertPresetCommand.Execute(new PresetAsk(row, preset));
    }

    // Only rows on screen get here. Each recycled row is given the look of the file it now shows.
    void OnContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not LibraryRowViewModel row) return;
        var item = (ListViewItem)args.ItemContainer;
        item.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        item.MinHeight = 60;
        item.Padding = new Thickness(4);
        AutomationProperties.SetName(item, $"{row.Title}: {row.Status}");
        if (item.ContentTemplateRoot is ContentControl host) host.Content = RowLook(row);
        args.Handled = true;
    }

    static Display RowLook(LibraryRowViewModel row)
    {
        var tone = row.IsMissing ? DisplayTone.Amber : DisplayTone.Cyan;
        var lines = new StackPanel();
        lines.Children.Add(new GlowText { Tone = tone, Text = row.Title.ToUpperInvariant() });
        lines.Children.Add(new GlowText { Tone = tone, Dimmed = true, Text = row.Status });
        return new Display { Tone = tone, Height = 60, Padding = new Thickness(12, 5, 12, 5), Content = lines };
    }

    // A reset (a search, or a big scan) drops the list's selection; the same file is chosen again if it's still listed.
    void OnRows(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Selected is null && _selectedFile is { } id && _library.Rows.FirstOrDefault(r => r.FileId == id) is { } again)
            _list.SelectedItem = again;
        PaintActions();
        PaintCount();
    }

    void OnLibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        PaintActions();
        if (e.PropertyName is nameof(LibraryViewModel.CountText) or nameof(LibraryViewModel.ScanText) or nameof(LibraryViewModel.SpaceText)) PaintCount();
    }

    void PaintCount() => _count.Text = string.Join(" · ", new[] { _library.CountText, _library.ScanText, _library.SpaceText }.Where(t => t.Length > 0));

    void PaintInk() => _message.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);

    void PaintActions()
    {
        var row = Selected;
        bool any = !_library.IsEmpty;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _open.IsEnabled = _reveal.IsEnabled = row?.CanOpen == true;
        _car.IsEnabled = _phone.IsEnabled = _archive.IsEnabled = _shrink.IsEnabled = row?.CanConvert == true;
        _remove.IsEnabled = row?.CanRemove == true && !_library.IsClearing;
        _clearMissing.IsEnabled = any && !_library.IsClearing;
        _message.Text = _library.Message;
        _message.Visibility = _library.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
