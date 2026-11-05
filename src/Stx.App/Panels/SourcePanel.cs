using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Stx.App.Panels;

/// <summary>Where a link goes in, and where its problems come back out with a next step.</summary>
public sealed partial class SourcePanel : ContentControl
{
    readonly SourceViewModel _source;
    readonly DisplayTextBox _box = new() { Label = "Link", PlaceholderText = "PASTE LINK OR CAPTURE FROM BROWSER…" };
    readonly Pad _analyze = new() { Content = "ANALYZE", Lit = true, LitTone = PadTone.Blue };
    readonly StateView _state = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock _next = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Visibility = Visibility.Collapsed };
    readonly Grid _offer = new() { Visibility = Visibility.Collapsed, ColumnSpacing = 16 };
    readonly TextBlock _offerText = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    readonly Pad _add = new() { Lit = true, Content = "ADD ALL" };
    readonly Pad _single = new() { Content = "JUST THIS VIDEO" };
    readonly ClipboardOfferViewModel? _clipboard;
    readonly Grid _clipRow = new() { Visibility = Visibility.Collapsed, ColumnSpacing = 16 };
    readonly TextBlock _clipText = new() { FontFamily = new FontFamily("Segoe UI"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

    /// <param name="playStream">PLAY STREAM: plays the link in the box on the PLAYER plate, as a stream or radio station.</param>
    /// <param name="playDisc">PLAY DISC: picks a DVD or Blu-ray drive, or a disc folder, and plays it.</param>
    public SourcePanel(SourceViewModel source, ClipboardOfferViewModel? clipboard = null, Func<Task>? playStream = null, Func<Task>? playDisc = null)
    {
        _source = source;
        _clipboard = clipboard;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var paste = new Pad { Content = "PASTE" };
        paste.Click += async (_, _) => await PasteAsync();
        _analyze.Click += async (_, _) => await source.AnalyzeCommand.ExecuteAsync(null);
        _box.TextChanged += (_, _) => { if (source.Link != _box.Text) source.Link = _box.Text; };
        _box.KeyDown += async (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            await source.AnalyzeCommand.ExecuteAsync(null);
        };

        var line = new Grid { ColumnSpacing = 16 };
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var field = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(6), Content = _box, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_analyze, 1);
        Grid.SetColumn(paste, 2);
        line.Children.Add(field);
        line.Children.Add(_analyze);
        line.Children.Add(paste);

        var decline = new Pad { Content = "CANCEL" };
        _single.Click += async (_, _) => await source.JustThisVideoCommand.ExecuteAsync(null);
        _add.Click += (_, _) => source.ConfirmPlaylistCommand.Execute(null);
        decline.Click += (_, _) => source.DeclinePlaylistCommand.Execute(null);
        _offer.ColumnDefinitions.Add(new ColumnDefinition());
        _offer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _offer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _offer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_single, 1);
        Grid.SetColumn(_add, 2);
        Grid.SetColumn(decline, 3);
        _offer.Children.Add(_offerText);
        _offer.Children.Add(_single);
        _offer.Children.Add(_add);
        _offer.Children.Add(decline);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Source" });
        column.Children.Add(line);
        if (playStream is not null || playDisc is not null)
        {
            // Playing straight from a stream or a disc, without saving anything. Web pages keep using ANALYZE.
            var play = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            if (playStream is not null)
            {
                var stream = new Pad { Content = "PLAY STREAM" };
                stream.Click += async (_, _) => await playStream();
                play.Children.Add(stream);
            }
            if (playDisc is not null)
            {
                var disc = new Pad { Content = "PLAY DISC" };
                disc.Click += async (_, _) => await playDisc();
                play.Children.Add(disc);
            }
            column.Children.Add(play);
        }
        column.Children.Add(_state);
        column.Children.Add(_next);
        column.Children.Add(_offer);
        column.Children.Add(_clipRow);
        Content = new Faceplate { PanelName = "source", Padding = new Thickness(36, 28, 36, 28), Content = column };

        if (clipboard is not null) BuildClipboardRow(clipboard, source);
        source.PropertyChanged += (_, _) => Paint();
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
        Paint();
    }

    void BuildClipboardRow(ClipboardOfferViewModel clipboard, SourceViewModel source)
    {
        var take = new Pad { Content = "GRAB IT", Lit = true, LitTone = PadTone.Blue };
        var dismiss = new Pad { Content = "NO THANKS" };
        take.Click += async (_, _) =>
        {
            source.Link = clipboard.Take() ?? "";
            await source.AnalyzeCommand.ExecuteAsync(null);
        };
        dismiss.Click += (_, _) => clipboard.Dismiss();
        _clipRow.ColumnDefinitions.Add(new ColumnDefinition());
        _clipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _clipRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(take, 1);
        Grid.SetColumn(dismiss, 2);
        _clipRow.Children.Add(_clipText);
        _clipRow.Children.Add(take);
        _clipRow.Children.Add(dismiss);
        clipboard.PropertyChanged += (_, _) =>
        {
            _clipText.Text = clipboard.OfferedLink is { } link ? "Copied link: " + link : "";
            _clipRow.Visibility = clipboard.OfferedLink is null ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    void PaintInk()
    {
        var ink = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);
        _next.Foreground = ink;
        _offerText.Foreground = ink;
        _clipText.Foreground = ink;
    }

    void Paint()
    {
        if (_box.Text != _source.Link) _box.Text = _source.Link;
        _analyze.IsEnabled = !_source.IsBusy;

        bool showing = _source.IsBusy || _source.Message.Length > 0;
        _state.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
        if (showing)
        {
            _state.State = _source.IsBusy ? ViewState.Loading : ViewState.Error;
            _state.Message = _source.IsBusy ? "READING LINK…" : _source.Message.ToUpperInvariant();
        }

        _next.Text = _source.NextStep;
        _next.Visibility = _source.NextStep.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _single.Visibility = _source.PendingPlaylist?.SingleVideoUrl is null ? Visibility.Collapsed : Visibility.Visible;
        if (_source.PendingPlaylist is { } offer)
        {
            _offerText.Text = offer.Unavailable > 0
                ? $"{offer.Title}: {offer.Available} items to add. {offer.Unavailable} private or unavailable, so skipped."
                : $"{offer.Title}: {offer.Available} items to add.";
            _add.Content = $"ADD {offer.Available}";
            _offer.Visibility = Visibility.Visible;
        }
        else
        {
            _offer.Visibility = Visibility.Collapsed;
        }
    }

    async Task PasteAsync()
    {
        var clip = Clipboard.GetContent();
        if (!clip.Contains(StandardDataFormats.Text)) return;
        _source.Link = (await clip.GetTextAsync()).Trim();
        await _source.AnalyzeCommand.ExecuteAsync(null);
    }
}
