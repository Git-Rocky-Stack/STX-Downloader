using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Look;

namespace Stx.App.Gallery;

/// <summary>Fixed pages for capture and baseline comparison, one per primitive.</summary>
internal static class GalleryPages
{
    public static UIElement Build(GalleryOptions options) => options.Name switch
    {
        "faceplate" => Faceplates(),
        "display" => Displays(),
        _ => new Grid(),
    };

    static UIElement Faceplates()
    {
        var page = new Grid { RowSpacing = 8, Padding = new Thickness(8) };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(380) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });

        var large = new Faceplate { PanelName = "gallery-large" };
        var small = new Faceplate
        {
            PanelName = "gallery-small",
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetRow(small, 1);
        page.Children.Add(large);
        page.Children.Add(small);
        return page;
    }

    static UIElement Displays()
    {
        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(new Display
        {
            Tone = DisplayTone.Amber,
            Height = 104,
            Content = Lines(DisplayTone.Amber, ("SOURCE   OPUS 160 KBPS · 48 KHZ", false), ("OUTPUT   ORIGINAL · NO RE-ENCODE", false), ("QUALITY  BIT-PERFECT", false)),
        });
        column.Children.Add(new Display
        {
            Tone = DisplayTone.Cyan,
            Height = 76,
            Content = Lines(DisplayTone.Cyan, ("SAMPLE MUSIC VIDEO", false), ("BIT-PERFECT · 6.2 MB/S", true)),
        });
        column.Children.Add(new Display
        {
            Tone = DisplayTone.Cyan,
            Height = 56,
            Content = Lines(DisplayTone.Cyan, ("PASTE LINK OR CAPTURE FROM BROWSER…", true)),
        });

        var page = new Grid { Padding = new Thickness(8) };
        page.Children.Add(new Faceplate { PanelName = "gallery-display", Content = column });
        return page;
    }

    static StackPanel Lines(DisplayTone tone, params (string Text, bool Dimmed)[] lines)
    {
        var stack = new StackPanel();
        foreach (var (text, dimmed) in lines)
            stack.Children.Add(new GlowText { Text = text, Tone = tone, Dimmed = dimmed });
        return stack;
    }
}
