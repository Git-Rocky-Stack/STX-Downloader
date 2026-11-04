using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Core.Jobs;
using Stx.Look;
using Stx.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Stx.App.Panels;

/// <summary>One job on the queue: LED, name and status behind glass, meter and readout.</summary>
public sealed partial class QueueRow : Grid
{
    readonly QueueItemViewModel _item;
    readonly Led _led = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ScrollingGlowText _title = new() { Tone = DisplayTone.Cyan };
    readonly GlowText _status = new() { Tone = DisplayTone.Cyan, Dimmed = true };
    bool _selected, _pointerOver;
    readonly Bargraph _meter = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly SevenSegmentReadout _readout = new() { VerticalAlignment = VerticalAlignment.Center };

    public QueueRow(QueueItemViewModel item)
    {
        _item = item;
        ColumnSpacing = 16;
        Padding = new Thickness(4);
        MinHeight = 72;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lines = new StackPanel();
        _title.Text = item.Title.ToUpperInvariant();
        lines.Children.Add(_title);
        lines.Children.Add(_status);
        var screen = new Display { Tone = DisplayTone.Cyan, Height = 64, Padding = new Thickness(12, 6, 12, 6), Content = lines };

        SetColumn(screen, 1);
        SetColumn(_meter, 2);
        SetColumn(_readout, 3);
        Children.Add(_led);
        Children.Add(screen);
        Children.Add(_meter);
        Children.Add(_readout);

        PointerEntered += (_, _) => { _pointerOver = true; PaintScrolling(); };
        PointerExited += (_, _) => { _pointerOver = false; PaintScrolling(); };
        item.PropertyChanged += OnChanged;
        Paint();
    }

    /// <summary>The row is the selected one in its list.</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PaintScrolling(); }
    }

    void PaintScrolling()
    {
        _title.Working = _item.State is JobState.Queued or JobState.Running or JobState.Analyzing or JobState.Waiting;
        _title.LookedAt = _selected || _pointerOver;
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e) => Paint();

    void Paint()
    {
        _led.Color = _item.LedColor;
        if (_led.State != _item.LedState && !(_led.State == LedState.Busy && _item.LedState == LedState.Strike)) _led.State = _item.LedState;
        _status.Text = _item.Status;
        _meter.Value = _item.Percent;
        _readout.Text = ((int)Math.Round(_item.Percent)).ToString(CultureInfo.InvariantCulture);
        PaintScrolling();
    }
}

/// <summary>The queue: a native list, so selection, scrolling and keyboard use need nothing extra.</summary>
public sealed partial class QueuePanel : ContentControl
{
    readonly QueueViewModel _queue;
    readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), MaxHeight = 420 };
    readonly StateView _empty = new() { State = ViewState.Empty, Message = "NOTHING IN THE QUEUE" };
    readonly Pad _pause = new() { Content = "PAUSE" }, _resume = new() { Content = "RESUME" }, _cancel = new() { Content = "CANCEL" }, _retry = new() { Content = "RETRY" };
    readonly Pad _reopen = new() { Content = "OPEN PAGE", Lit = true, LitTone = PadTone.Blue }, _copyLog = new() { Content = "COPY LOG" };
    readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Visibility = Visibility.Collapsed };

    public QueuePanel(QueueViewModel queue)
    {
        _queue = queue;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_list, "Queue");
        // The console's own blue, or the selection marker takes the Windows accent color.
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
            _list.Resources[$"ListViewItemSelectionIndicator{(state.Length == 0 ? "" : state)}Brush"] = LookBrushes.From("#58C7FF");

        _list.SelectionChanged += (_, _) =>
        {
            foreach (QueueRowHost host in _list.Items) host.Row.Selected = ReferenceEquals(host, _list.SelectedItem);
            PaintActions();
        };

        _pause.Click += (_, _) => Act(queue.Pause);
        _resume.Click += (_, _) => Act(queue.Resume);
        _cancel.Click += (_, _) => Act(queue.Cancel);
        _retry.Click += (_, _) => Act(queue.Retry);
        _reopen.Click += (_, _) => Act(queue.Reopen);
        _copyLog.Click += (_, _) => CopyLog();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var pad in new[] { _pause, _resume, _cancel, _retry, _reopen, _copyLog }) actions.Children.Add(pad);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Queue" });
        column.Children.Add(_empty);
        column.Children.Add(_list);
        column.Children.Add(_detail);
        column.Children.Add(actions);
        Content = new Faceplate { PanelName = "queue", Padding = new Thickness(36, 28, 36, 28), Content = column };

        queue.Items.CollectionChanged += OnItems;
        foreach (var item in queue.Items) AddRow(item);
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
        PaintActions();
    }

    QueueItemViewModel? Selected => (_list.SelectedItem as QueueRowHost)?.Item;

    void OnItems(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (QueueItemViewModel item in e.NewItems ?? Array.Empty<QueueItemViewModel>()) AddRow(item);
        PaintActions();
    }

    void AddRow(QueueItemViewModel item)
    {
        var host = new QueueRowHost(item);
        _list.Items.Add(host);
        item.PropertyChanged += (_, _) => { if (ReferenceEquals(Selected, item)) PaintActions(); };
        if (_list.SelectedItem is null) _list.SelectedItem = host;
    }

    void Act(Action<QueueItemViewModel> action)
    {
        if (Selected is { } item) action(item);
        PaintActions();
    }

    void CopyLog()
    {
        if (Selected is not { } item) return;
        var package = new DataPackage();
        package.SetText(item.RawLog);
        Clipboard.SetContent(package);
    }

    void PaintInk() => _detail.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);

    void PaintActions()
    {
        var item = Selected;
        _empty.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = _list.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _pause.IsEnabled = item?.CanPause == true;
        _resume.IsEnabled = item?.CanResume == true;
        _cancel.IsEnabled = item?.CanCancel == true;
        _retry.IsEnabled = item?.CanRetry == true;
        _reopen.Visibility = item?.ShowsReopen == true ? Visibility.Visible : Visibility.Collapsed;
        _copyLog.IsEnabled = item is not null;

        string words = item is null ? "" : $"{item.ErrorMessage} {item.NextStep}".Trim();
        _detail.Text = words;
        _detail.Visibility = words.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The list item itself, so the name a screen reader announces is on the element it actually reads.</summary>
    sealed partial class QueueRowHost : ListViewItem
    {
        public QueueRowHost(QueueItemViewModel item)
        {
            Item = item;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Row = new QueueRow(item);
            Content = Row;
            item.PropertyChanged += (_, _) => Announce();
            Announce();
        }

        public QueueItemViewModel Item { get; }

        public QueueRow Row { get; }

        void Announce() => AutomationProperties.SetName(this, $"{Item.Title}: {Item.Status}");
    }
}
