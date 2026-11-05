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
        MaxHeight = 420,
        ItemTemplate = (DataTemplate)XamlReader.Load(RowTemplate),
    };
    readonly StateView _empty = new() { State = ViewState.Empty, Message = "NOTHING SAVED YET" };
    readonly DisplayTextBox _search = new() { Label = "Search the library", PlaceholderText = "SEARCH TITLE, FILE OR FOLDER…", Tone = DisplayTone.Cyan };
    readonly GlowText _count = new() { Tone = DisplayTone.Cyan, Dimmed = true, TextSize = 13 };
    readonly Pad _open = new() { Content = "OPEN" }, _reveal = new() { Content = "FIND FILE" }, _car = new() { Content = "CAR USB" }, _phone = new() { Content = "PHONE" }, _archive = new() { Content = "ARCHIVE" };
    readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Visibility = Visibility.Collapsed };
    long? _selectedFile;

    public LibraryPanel(LibraryViewModel library, Action<LibraryRowViewModel?> selected)
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

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var pad in new[] { _open, _reveal, _car, _phone, _archive }) actions.Children.Add(pad);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Library" });
        column.Children.Add(new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(6), Content = _search });
        column.Children.Add(_count);
        column.Children.Add(_empty);
        column.Children.Add(_list);
        column.Children.Add(_message);
        column.Children.Add(actions);
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
        if (e.PropertyName is nameof(LibraryViewModel.CountText) or nameof(LibraryViewModel.ScanText)) PaintCount();
    }

    void PaintCount() => _count.Text = _library.ScanText.Length > 0 ? $"{_library.CountText} · {_library.ScanText}" : _library.CountText;

    void PaintInk() => _message.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);

    void PaintActions()
    {
        var row = Selected;
        bool any = !_library.IsEmpty;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _open.IsEnabled = _reveal.IsEnabled = row?.CanOpen == true;
        _car.IsEnabled = _phone.IsEnabled = _archive.IsEnabled = row?.CanConvert == true;
        _message.Text = _library.Message;
        _message.Visibility = _library.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
