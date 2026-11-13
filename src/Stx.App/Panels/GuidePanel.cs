using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>
/// The user guide, on its own plate, so it opens without leaving the console. A quick start across the top; under it an
/// index of sections behind glass, and the page for the one chosen: its heading, a lead line, the details at a readable
/// measure, the console's names it uses as chips, and SHOW ME to run that part's tutorial on the real controls.
/// PREVIOUS and NEXT turn the page. Every pad, switch, knob and readout a line names is set in the accent.
/// (Review 2026-11-12: three columns of every section at once read as a wall of text.)
/// </summary>
public sealed partial class GuidePanel : ContentControl
{
    // On the 8-point grid. Body text at 14/22 and a measure of about 75 characters; the lead a step larger.
    const double BodySize = 14, BodyLine = 22, LeadSize = 15, LeadLine = 24, HeadingSize = 24, MarkerSize = 5;
    const double PageMeasure = 720, IndexWidth = 320, IndexRow = 44, NarrowBelow = 900, QuickFourAcross = 1000;

    readonly Action<Tour>? _showMe;
    readonly DisplayList _index = new() { Label = "Guide sections", Tone = DisplayTone.Cyan };
    readonly Display _indexGlass;
    readonly StackPanel _page = new() { Spacing = 16, MaxWidth = PageMeasure, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _body = new() { ColumnSpacing = 40, RowSpacing = 24 };
    readonly Grid _quick = new() { ColumnSpacing = 16, RowSpacing = 16 };
    readonly Pad _previous = new() { Content = "PREVIOUS" }, _next = new() { Content = "NEXT" }, _show = new() { Content = "SHOW ME", Lit = true, LitTone = PadTone.Blue, HorizontalAlignment = HorizontalAlignment.Left };
    readonly InkSet _fixed = new(), _pageInk = new();
    int _section = -1, _quickAcross;
    bool _narrow;

    /// <param name="close">CLOSE: puts the plate away.</param>
    /// <param name="showMe">SHOW ME: runs the tutorial for the section on the page. Null hides the pad.</param>
    public GuidePanel(Action close, Action<Tour>? showMe = null)
    {
        _showMe = showMe;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var shut = new Pad { Content = "CLOSE", VerticalAlignment = VerticalAlignment.Top };
        shut.Click += (_, _) => close();
        var name = new EngravedLabel { Kind = LabelKind.Title, Text = "User guide" };
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level1);
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(name);
        heading.Children.Add(Words(GuideContent.Reopen, GuideContent.ReopenControls, _fixed.Dim, BodySize, BodyLine));
        var title = new Grid { ColumnSpacing = 16 };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(shut, 1);
        title.Children.Add(heading);
        title.Children.Add(shut);

        // The index: a glass display listing every section, as the console lists what matters.
        _index.ItemsSource = GuideContent.Sections.Select(s => s.Title.ToUpperInvariant()).ToList();
        _index.SelectionChanged += (_, _) => { if (_index.SelectedIndex >= 0) ShowSection(_index.SelectedIndex); };
        _indexGlass = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(4), Content = _index, VerticalAlignment = VerticalAlignment.Top };

        _previous.Click += (_, _) => Turn(-1);
        _next.Click += (_, _) => Turn(+1);
        _show.Click += (_, _) => { if (_section >= 0 && TutorialContent.ForGuideSection(GuideContent.Sections[_section].Title) is { } tour) _showMe?.Invoke(tour); };

        _body.SizeChanged += (_, e) => Lay(e.NewSize.Width);
        _body.Children.Add(_indexGlass);
        _body.Children.Add(_page);

        var column = new StackPanel { Spacing = 32 };
        column.Children.Add(title);
        column.Children.Add(QuickStart());
        column.Children.Add(_body);
        Content = new Faceplate { PanelName = "guide", Padding = new Thickness(36, 28, 36, 36), Content = column };

        Lay(1200);
        _index.SelectedIndex = 0;
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
    }

    // The index beside the page on a wide plate; above it, shorter and scrolling, on a narrow one.
    void Lay(double width)
    {
        bool narrow = width < NarrowBelow;
        if (narrow == _narrow && _body.ColumnDefinitions.Count > 0) return;
        _narrow = narrow;
        _body.ColumnDefinitions.Clear();
        _body.RowDefinitions.Clear();
        int sections = GuideContent.Sections.Count;
        if (narrow)
        {
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_page, 0);
            Grid.SetRow(_page, 1);
            _index.Height = IndexRow * Math.Min(sections, 5) + 8;
            _page.MinHeight = 0;
        }
        else
        {
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IndexWidth) });
            _body.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(_page, 1);
            Grid.SetRow(_page, 0);
            _index.Height = IndexRow * sections + 8;
            // As tall as the index, so turning to a short page doesn't make the plate jump.
            _page.MinHeight = _index.Height + 8;
        }
    }

    /// <summary>Turns to the section with this title, as the gallery shows one.</summary>
    public void ShowSectionTitled(string title)
    {
        int index = GuideContent.Sections.ToList().FindIndex(s => s.Title == title);
        if (index >= 0) _index.SelectedIndex = index;
    }

    void Turn(int by)
    {
        int next = Math.Clamp(_section + by, 0, GuideContent.Sections.Count - 1);
        if (next != _section) _index.SelectedIndex = next;
    }

    void ShowSection(int index)
    {
        _section = index;
        var section = GuideContent.Sections[index];
        _pageInk.Clear();
        _page.Children.Clear();

        var where = new EngravedLabel { Kind = LabelKind.Sub, Text = $"Section {index + 1} of {GuideContent.Sections.Count}" };
        var heading = new TextBlock
        {
            Text = section.Title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = HeadingSize,
            LineHeight = Math.Round(HeadingSize * 1.2),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        _pageInk.Ink.Add(heading);
        var lead = Words(section.Summary, section.Marked, _pageInk.Dim, LeadSize, LeadLine);
        var rule = new Rectangle { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        _pageInk.Rules.Add(rule);

        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(where);
        top.Children.Add(heading);
        top.Children.Add(lead);
        _page.Children.Add(top);
        _page.Children.Add(rule);

        var points = new StackPanel { Spacing = 12 };
        foreach (string line in section.Lines) points.Children.Add(Point(line, section.Marked));
        _page.Children.Add(points);

        if (section.Controls.Count > 0)
        {
            var chips = new PadRow { Spacing = 8 };
            foreach (string control in section.Controls)
                chips.Children.Add(new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(10, 4, 10, 4), Content = new GlowText { Tone = DisplayTone.Cyan, TextSize = 12, Text = control } });
            var named = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            named.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "On the console" });
            named.Children.Add(chips);
            AutomationProperties.SetName(chips, "Controls this section names: " + string.Join(", ", section.Controls));
            _page.Children.Add(named);
        }

        var tour = _showMe is null ? null : TutorialContent.ForGuideSection(section.Title);
        _show.Visibility = tour is null ? Visibility.Collapsed : Visibility.Visible;
        if (tour is not null) AutomationProperties.SetName(_show, $"Show me: run the {tour.Title} tutorial");
        _previous.IsEnabled = index > 0;
        _next.IsEnabled = index < GuideContent.Sections.Count - 1;
        var turn = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        Detach(_previous);
        Detach(_next);
        Detach(_show);
        turn.Children.Add(_previous);
        turn.Children.Add(_next);
        var foot = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(turn, 1);
        foot.Children.Add(_show);
        foot.Children.Add(turn);
        _page.Children.Add(foot);

        PaintInk();
        if ((FrameworkElementAutomationPeer.FromElement(heading) ?? FrameworkElementAutomationPeer.CreatePeerForElement(heading)) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel parent) parent.Children.Remove(element);
    }

    // Four numbered steps behind glass, as the console shows what matters, each with its sentence on the metal below.
    Grid QuickStart()
    {
        var block = new Grid { RowSpacing = 16 };
        block.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        block.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new EngravedLabel { Kind = LabelKind.Label, Text = "Quick start" };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        block.Children.Add(heading);
        for (int i = 0; i < GuideContent.QuickStart.Count; i++)
        {
            var step = GuideContent.QuickStart[i];
            var digits = new GlowText { Tone = DisplayTone.Cyan, TextSize = 24, Text = (i + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
            var label = new GlowText { Tone = DisplayTone.Cyan, Text = step.Title.ToUpperInvariant(), VerticalAlignment = VerticalAlignment.Center };
            var face = new Grid { ColumnSpacing = 14 };
            face.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            face.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(label, 1);
            face.Children.Add(digits);
            face.Children.Add(label);
            var glass = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(14, 8, 14, 8), Content = face };
            AutomationProperties.SetName(glass, $"Step {i + 1}: {step.Title}");
            var card = new StackPanel { Spacing = 12 };
            card.Children.Add(glass);
            card.Children.Add(Words(step.Line, step.Marked, _fixed.Ink, BodySize, BodyLine));
            _quick.Children.Add(card);
        }
        _quick.SizeChanged += (_, e) => LayQuick(e.NewSize.Width);
        LayQuick(1200);
        Grid.SetRow(_quick, 1);
        block.Children.Add(_quick);
        return block;
    }

    void LayQuick(double width)
    {
        int across = width >= QuickFourAcross ? 4 : 2;
        if (across == _quickAcross) return;
        _quickAcross = across;
        _quick.ColumnDefinitions.Clear();
        _quick.RowDefinitions.Clear();
        for (int c = 0; c < across; c++) _quick.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < (_quick.Children.Count + across - 1) / across; r++) _quick.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < _quick.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)_quick.Children[i], i % across);
            Grid.SetRow((FrameworkElement)_quick.Children[i], i / across);
        }
    }

    // A line with a small square marker hung beside its first line of text, so wrapped lines stay aligned.
    Grid Point(string line, IReadOnlyList<string> marked)
    {
        var marker = new Rectangle
        {
            Width = MarkerSize,
            Height = MarkerSize,
            RadiusX = 1,
            RadiusY = 1,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, (BodyLine - MarkerSize) / 2, 0, 0),
        };
        AutomationProperties.SetAccessibilityView(marker, AccessibilityView.Raw);
        _pageInk.Markers.Add(marker);
        var text = Words(line, marked, _pageInk.Ink, BodySize, BodyLine);
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(text, 1);
        row.Children.Add(marker);
        row.Children.Add(text);
        return row;
    }

    // Body text with the console's names set in the accent, semibold, so they read as the labels they are.
    TextBlock Words(string line, IReadOnlyList<string> marked, List<TextBlock> ink, double size, double height)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = size,
            LineHeight = height,
        };
        var accent = ReferenceEquals(ink, _fixed.Ink) || ReferenceEquals(ink, _fixed.Dim) ? _fixed.Accent : _pageInk.Accent;
        foreach (var span in GuideContent.Spans(line, marked))
        {
            var run = new Run { Text = span.Text };
            if (span.Marked)
            {
                run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                accent.Add(run);
            }
            text.Inlines.Add(run);
        }
        ink.Add(text);
        return text;
    }

    void PaintInk()
    {
        var tokens = LookTokens.For(LookState.Mode);
        Brush ink = LookBrushes.From(tokens["Ink"]), dim = LookBrushes.From(tokens["InkDim"]), accent = LookBrushes.From(tokens["Accent"]), rule = LookBrushes.From(tokens["EdgeHighlight"]);
        _fixed.Paint(ink, dim, accent, rule);
        _pageInk.Paint(ink, dim, accent, rule);
    }

    /// <summary>What a part of the plate paints from the look's tokens, so a page turned to drops what it held.</summary>
    sealed class InkSet
    {
        public readonly List<TextBlock> Ink = [], Dim = [];
        public readonly List<Run> Accent = [];
        public readonly List<Rectangle> Markers = [], Rules = [];

        public void Clear()
        {
            Ink.Clear();
            Dim.Clear();
            Accent.Clear();
            Markers.Clear();
            Rules.Clear();
        }

        public void Paint(Brush ink, Brush dim, Brush accent, Brush rule)
        {
            foreach (var text in Ink) text.Foreground = ink;
            foreach (var text in Dim) text.Foreground = dim;
            foreach (var run in Accent) run.Foreground = accent;
            foreach (var marker in Markers) marker.Fill = accent;
            foreach (var line in Rules) line.Fill = rule;
        }
    }
}
