using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>Everything saved, as a native list. Selecting a file shows its truth in the panel beside it.</summary>
public sealed partial class LibraryPanel : ContentControl
{
    readonly LibraryViewModel _library;
    readonly Action<LibraryRowViewModel?> _selected;
    readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), MaxHeight = 300 };
    readonly StateView _empty = new() { State = ViewState.Empty, Message = "NOTHING SAVED YET" };
    readonly Pad _open = new() { Content = "OPEN" }, _reveal = new() { Content = "FIND FILE" }, _car = new() { Content = "CAR USB" }, _phone = new() { Content = "PHONE" }, _archive = new() { Content = "ARCHIVE" };
    readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Visibility = Visibility.Collapsed };

    public LibraryPanel(LibraryViewModel library, Action<LibraryRowViewModel?> selected)
    {
        _library = library;
        _selected = selected;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_list, "Library");
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            _list.Resources[$"ListViewItemSelectionIndicator{state}Brush"] = LookBrushes.From("#58C7FF");

        _list.SelectionChanged += (_, _) => { PaintActions(); _selected(Selected); };
        _open.Click += (_, _) => library.OpenCommand.Execute(Selected);
        _reveal.Click += (_, _) => library.RevealCommand.Execute(Selected);
        _car.Click += (_, _) => Convert("Car USB");
        _phone.Click += (_, _) => Convert("Phone");
        _archive.Click += (_, _) => Convert("Archive");

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var pad in new[] { _open, _reveal, _car, _phone, _archive }) actions.Children.Add(pad);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Library" });
        column.Children.Add(_empty);
        column.Children.Add(_list);
        column.Children.Add(_message);
        column.Children.Add(actions);
        Content = new Faceplate { PanelName = "library", Padding = new Thickness(36, 28, 36, 28), Content = column };

        library.Rows.CollectionChanged += OnRows;
        library.PropertyChanged += (_, _) => PaintActions();
        Rebuild();
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
    }

    LibraryRowViewModel? Selected => (_list.SelectedItem as RowHost)?.Row;

    void Convert(string preset)
    {
        if (Selected is { } row) _library.ConvertPresetCommand.Execute(new PresetAsk(row, preset));
    }

    void OnRows(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void Rebuild()
    {
        long? keep = Selected?.FileId;
        _list.Items.Clear();
        foreach (var row in _library.Rows)
        {
            var host = new RowHost(row);
            _list.Items.Add(host);
            if (row.FileId == keep) _list.SelectedItem = host;
        }
        PaintActions();
    }

    void PaintInk() => _message.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);

    void PaintActions()
    {
        var row = Selected;
        bool any = _list.Items.Count > 0;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        _list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _open.IsEnabled = _reveal.IsEnabled = row?.CanOpen == true;
        _car.IsEnabled = _phone.IsEnabled = _archive.IsEnabled = row?.CanConvert == true;
        _message.Text = _library.Message;
        _message.Visibility = _library.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    sealed partial class RowHost : ListViewItem
    {
        public RowHost(LibraryRowViewModel row)
        {
            Row = row;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            MinHeight = 60;
            Padding = new Thickness(4);
            AutomationProperties.SetName(this, $"{row.Title}: {row.Status}");
            var lines = new StackPanel();
            lines.Children.Add(new GlowText { Tone = row.IsMissing ? DisplayTone.Amber : DisplayTone.Cyan, Text = row.Title.ToUpperInvariant() });
            lines.Children.Add(new GlowText { Tone = row.IsMissing ? DisplayTone.Amber : DisplayTone.Cyan, Dimmed = true, Text = row.Status });
            Content = new Display { Tone = row.IsMissing ? DisplayTone.Amber : DisplayTone.Cyan, Height = 60, Padding = new Thickness(12, 5, 12, 5), Content = lines };
        }

        public LibraryRowViewModel Row { get; }
    }
}
