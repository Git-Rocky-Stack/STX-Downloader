using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>
/// The tutorials: a chapter card for each guided tour of the live console (TutorialContent), with its lamp lit green
/// once finished and a START pad that runs it on the real controls (TourOverlay). Nothing here is a recording, so it
/// can't fall behind the console.
/// </summary>
public sealed partial class TutorialPanel : ContentControl
{
    // Cards wrap to the plate: three across on a wide one, two, then one. On the 8-point grid.
    const double CardGap = 24, ThreeAcross = 1040, TwoAcross = 640;

    readonly Func<string, bool> _done;
    readonly Grid _cards = new() { ColumnSpacing = CardGap, RowSpacing = CardGap };
    readonly List<(Tour Tour, Led Lamp, EngravedLabel State)> _chapters = [];
    readonly GlowText _tally = new() { Tone = DisplayTone.Cyan, TextSize = 13 };
    readonly Bargraph _overall = new() { Zones = BargraphZones.Progress, Height = 16, Segments = 28 };
    readonly List<TextBlock> _ink = [], _dim = [];
    int _across;

    /// <param name="close">CLOSE: puts the plate away.</param>
    /// <param name="start">START on a card: runs that chapter on the console.</param>
    /// <param name="done">Whether a chapter was finished, by id. Null in the gallery, where none is.</param>
    public TutorialPanel(Action close, Action<Tour> start, Func<string, bool>? done = null)
    {
        _done = done ?? (_ => false);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var shut = new Pad { Content = "CLOSE", VerticalAlignment = VerticalAlignment.Top };
        shut.Click += (_, _) => close();
        var name = new EngravedLabel { Kind = LabelKind.Title, Text = "Tutorials" };
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level1);
        var intro = Words("Guided tours of the live console. Pick a chapter: the console dims, and a spotlight walks you through each real control while you try it.", _dim);
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(name);
        heading.Children.Add(intro);
        var title = new Grid { ColumnSpacing = 16 };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(shut, 1);
        title.Children.Add(heading);
        title.Children.Add(shut);

        // How far through the series you are, on the same meter the Queue uses for a job.
        var progress = new Grid { ColumnSpacing = 16 };
        progress.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progress.ColumnDefinitions.Add(new ColumnDefinition { MaxWidth = 420 });
        _tally.VerticalAlignment = _overall.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_overall, 1);
        progress.Children.Add(_tally);
        progress.Children.Add(_overall);
        AutomationProperties.SetName(_overall, "Chapters finished");

        int number = 0;
        foreach (var tour in TutorialContent.Tours)
        {
            var card = Card(++number, tour, start, out var lamp, out var state);
            _chapters.Add((tour, lamp, state));
            _cards.Children.Add(card);
        }
        _cards.SizeChanged += (_, e) => Arrange(e.NewSize.Width);

        var column = new StackPanel { Spacing = 24 };
        column.Children.Add(title);
        column.Children.Add(progress);
        column.Children.Add(_cards);
        Content = new Faceplate { PanelName = "tutorial", Padding = new Thickness(36, 28, 36, 36), Content = column };

        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
        Refresh();
    }

    /// <summary>Reads again which chapters are finished: their lamps, and the tally.</summary>
    public void Refresh()
    {
        int finished = 0;
        foreach (var (tour, lamp, state) in _chapters)
        {
            bool done = _done(tour.Id);
            if (done) finished++;
            lamp.Color = LedColor.Green;
            lamp.State = done ? LedState.On : LedState.Off;
            state.Text = done ? "Finished" : "Not started";
            AutomationProperties.SetName(lamp, done ? $"{tour.Title}: finished" : $"{tour.Title}: not finished yet");
        }
        _tally.Text = $"{finished} OF {_chapters.Count} CHAPTERS FINISHED";
        _overall.Value = finished * 100.0 / Math.Max(1, _chapters.Count);
    }

    // A chapter: its number and title behind glass, what it covers on the metal, and its lamp beside START.
    FrameworkElement Card(int number, Tour tour, Action<Tour> start, out Led lamp, out EngravedLabel state)
    {
        var digits = new GlowText { Tone = DisplayTone.Cyan, TextSize = 26, Text = number.ToString("00", System.Globalization.CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
        var names = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new GlowText { Tone = DisplayTone.Cyan, Text = tour.Title.ToUpperInvariant() });
        names.Children.Add(new GlowText { Tone = DisplayTone.Cyan, Dimmed = true, TextSize = 12, Text = tour.Meta });
        var plate = new Grid { ColumnSpacing = 16 };
        plate.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        plate.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(names, 1);
        plate.Children.Add(digits);
        plate.Children.Add(names);
        var glass = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(16, 10, 16, 10), Content = plate };

        lamp = new Led { VerticalAlignment = VerticalAlignment.Center };
        state = new EngravedLabel { Kind = LabelKind.Label, VerticalAlignment = VerticalAlignment.Center };
        var go = new Pad { Content = "START", Lit = true, LitTone = PadTone.Blue };
        go.Click += (_, _) => start(tour);
        AutomationProperties.SetName(go, $"Start {tour.Title}");
        var foot = new Grid { ColumnSpacing = 12 };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(state, 1);
        Grid.SetColumn(go, 2);
        foot.Children.Add(lamp);
        foot.Children.Add(state);
        foot.Children.Add(go);

        // Glass on top, the summary taking whatever height the row gives it, and the lamp and START along the bottom,
        // so the cards in a row line up whatever their summaries' lengths.
        var card = new Grid { RowSpacing = 12 };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var summary = Words(tour.Summary, _ink);
        Grid.SetRow(summary, 1);
        Grid.SetRow(foot, 2);
        card.Children.Add(glass);
        card.Children.Add(summary);
        card.Children.Add(foot);
        AutomationProperties.SetName(card, $"Chapter {number}: {tour.Title}. {tour.Summary}");
        return card;
    }

    void Arrange(double width)
    {
        int across = width >= ThreeAcross ? 3 : width >= TwoAcross ? 2 : 1;
        if (across == _across) return;
        _across = across;
        _cards.ColumnDefinitions.Clear();
        _cards.RowDefinitions.Clear();
        for (int c = 0; c < across; c++) _cards.ColumnDefinitions.Add(new ColumnDefinition());
        int rows = (_cards.Children.Count + across - 1) / across;
        for (int r = 0; r < rows; r++) _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < _cards.Children.Count; i++)
        {
            var card = (FrameworkElement)_cards.Children[i];
            Grid.SetColumn(card, i % across);
            Grid.SetRow(card, i / across);
        }
    }

    static TextBlock Words(string text, List<TextBlock> ink)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            LineHeight = 22,
        };
        ink.Add(block);
        return block;
    }

    void PaintInk()
    {
        var tokens = LookTokens.For(LookState.Mode);
        Brush ink = LookBrushes.From(tokens["Ink"]), dim = LookBrushes.From(tokens["InkDim"]);
        foreach (var text in _ink) text.Foreground = ink;
        foreach (var text in _dim) text.Foreground = dim;
    }
}
