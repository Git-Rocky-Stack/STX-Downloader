using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>The user guide, on its own plate, so it opens without leaving the console.</summary>
public sealed partial class GuidePanel : ContentControl
{
    const double SectionSpacing = 24, LineSpacing = 8;

    readonly List<TextBlock> _lines = [];

    public GuidePanel(Action close)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var shut = new Pad { Content = "CLOSE", VerticalAlignment = VerticalAlignment.Center };
        shut.Click += (_, _) => close();
        var title = new Grid { ColumnSpacing = 16 };
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(shut, 1);
        var name = new EngravedLabel { Kind = LabelKind.Title, Text = "User guide", VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(name);
        title.Children.Add(shut);

        var columns = new Grid { ColumnSpacing = 40 };
        for (int i = 0; i < GuideContent.Columns.Count; i++)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            var stack = new StackPanel { Spacing = SectionSpacing };
            foreach (var section in GuideContent.Columns[i]) stack.Children.Add(Section(section));
            Grid.SetColumn(stack, i);
            columns.Children.Add(stack);
        }

        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(title);
        column.Children.Add(columns);
        Content = new Faceplate { PanelName = "guide", Padding = new Thickness(36, 28, 36, 28), Content = column };

        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => LookState.Changed -= PaintInk;
        PaintInk();
    }

    StackPanel Section(GuideSection section)
    {
        var block = new StackPanel { Spacing = LineSpacing };
        block.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = section.Title });
        foreach (string line in section.Lines)
        {
            var text = new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                LineHeight = 20,
            };
            _lines.Add(text);
            block.Children.Add(text);
        }
        return block;
    }

    void PaintInk()
    {
        var ink = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);
        foreach (var line in _lines) line.Foreground = ink;
    }
}
