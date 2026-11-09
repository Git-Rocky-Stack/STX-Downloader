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
/// The user guide, on its own plate, so it opens without leaving the console: a quick start across the top, then each
/// plate's section, one summary line and the details under it. Every pad, switch, knob and readout a line names is set
/// in the accent, so the line can be matched to the console at a glance.
/// </summary>
public sealed partial class GuidePanel : ContentControl
{
    // On the 8-point grid: 32 between sections, 16 between a title and what follows, 8 between lines.
    const double SectionSpacing = 32, LineSpacing = 8, ColumnSpacing = 40, BodySize = 13, BodyLine = 20, MarkerSize = 4;

    readonly List<TextBlock> _ink = [], _dim = [];
    readonly List<Run> _accent = [];
    readonly List<Rectangle> _markers = [];

    public GuidePanel(Action close)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var shut = new Pad { Content = "CLOSE", VerticalAlignment = VerticalAlignment.Top };
        shut.Click += (_, _) => close();
        var name = new EngravedLabel { Kind = LabelKind.Title, Text = "User guide" };
        AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level1);
        var heading = new StackPanel { Spacing = 8 };
        heading.Children.Add(name);
        heading.Children.Add(Words(GuideContent.Reopen, GuideContent.ReopenControls, _dim));
        var title = new Grid { ColumnSpacing = 16 };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(shut, 1);
        title.Children.Add(heading);
        title.Children.Add(shut);

        var columns = new Grid { ColumnSpacing = ColumnSpacing };
        for (int i = 0; i < GuideContent.Columns.Count; i++)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel { Spacing = SectionSpacing };
            foreach (var section in GuideContent.Columns[i]) stack.Children.Add(Section(section));
            Grid.SetColumn(stack, i);
            columns.Children.Add(stack);
        }

        var column = new StackPanel { Spacing = SectionSpacing };
        column.Children.Add(title);
        column.Children.Add(QuickStart());
        column.Children.Add(columns);
        Content = new Faceplate { PanelName = "guide", Padding = new Thickness(36, 28, 36, 36), Content = column };

        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
    }

    // Four numbered steps behind glass, as the console shows what matters, each with its sentence on the metal below.
    StackPanel QuickStart()
    {
        var steps = new Grid { ColumnSpacing = 16 };
        for (int i = 0; i < GuideContent.QuickStart.Count; i++)
        {
            var step = GuideContent.QuickStart[i];
            steps.ColumnDefinitions.Add(new ColumnDefinition());
            string label = $"{i + 1} · {step.Title.ToUpperInvariant()}";
            var glass = new Display
            {
                Tone = DisplayTone.Cyan,
                Padding = new Thickness(12, 8, 12, 8),
                Content = new GlowText { Tone = DisplayTone.Cyan, Text = label },
            };
            AutomationProperties.SetName(glass, $"Step {i + 1}: {step.Title}");
            var card = new StackPanel { Spacing = LineSpacing };
            card.Children.Add(glass);
            card.Children.Add(Words(step.Line, step.Marked, _ink));
            Grid.SetColumn(card, i);
            steps.Children.Add(card);
        }

        var block = new StackPanel { Spacing = 16 };
        block.Children.Add(Heading("Quick start"));
        block.Children.Add(steps);
        return block;
    }

    StackPanel Section(GuideSection section)
    {
        var block = new StackPanel { Spacing = LineSpacing };
        block.Children.Add(Heading(section.Title));
        var summary = Words(section.Summary, section.Marked, _dim);
        summary.Margin = new Thickness(0, 0, 0, 4);
        block.Children.Add(summary);
        foreach (string line in section.Lines) block.Children.Add(Point(line, section.Marked));
        return block;
    }

    static EngravedLabel Heading(string text)
    {
        var heading = new EngravedLabel { Kind = LabelKind.Label, Text = text };
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
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
        _markers.Add(marker);
        var text = Words(line, marked, _ink);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(text, 1);
        row.Children.Add(marker);
        row.Children.Add(text);
        return row;
    }

    // Body text with the console's names set in the accent, semibold, so they read as the labels they are.
    TextBlock Words(string line, IReadOnlyList<string> marked, List<TextBlock> ink)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = BodySize,
            LineHeight = BodyLine,
        };
        foreach (var span in GuideContent.Spans(line, marked))
        {
            var run = new Run { Text = span.Text };
            if (span.Marked)
            {
                run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                _accent.Add(run);
            }
            text.Inlines.Add(run);
        }
        ink.Add(text);
        return text;
    }

    void PaintInk()
    {
        var tokens = LookTokens.For(LookState.Mode);
        Brush ink = LookBrushes.From(tokens["Ink"]), dim = LookBrushes.From(tokens["InkDim"]), accent = LookBrushes.From(tokens["Accent"]);
        foreach (var text in _ink) text.Foreground = ink;
        foreach (var text in _dim) text.Foreground = dim;
        foreach (var run in _accent) run.Foreground = accent;
        foreach (var marker in _markers) marker.Fill = accent;
    }
}
