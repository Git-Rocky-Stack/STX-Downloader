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
        "led" => Leds(options.Hold),
        "bargraph" => Bargraphs(),
        "sevenseg" => SevenSegments(),
        "pad" => Pads(),
        "toggle" => Toggles(),
        "rotary" => Rotaries(),
        _ => new Grid(),
    };

    static UIElement Panel(string name, UIElement content)
    {
        var page = new Grid { Padding = new Thickness(8) };
        page.Children.Add(new Faceplate { PanelName = name, Padding = new Thickness(56, 48, 56, 48), Content = content });
        return page;
    }

    static UIElement SevenSegments()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32 };
        foreach (string text in new[] { "0", "62", "100", "888", "" })
            row.Children.Add(new SevenSegmentReadout { Text = text });
        return Panel("gallery-sevenseg", row);
    }

    static UIElement Pads()
    {
        var rows = new StackPanel { Spacing = 40 };

        var normal = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40 };
        normal.Children.Add(new Pad { Content = "PAUSE" });
        normal.Children.Add(new Pad { Content = "RUN", Lit = true });
        normal.Children.Add(new Pad { Content = "GO", Lit = true, LitTone = PadTone.Blue });
        rows.Children.Add(normal);

        var other = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40 };
        other.Children.Add(HeldDown(new Pad { Content = "HELD" }));
        other.Children.Add(HeldDown(new Pad { Content = "HELD", Lit = true }));
        other.Children.Add(new Pad { Content = "OFF", IsEnabled = false });
        other.Children.Add(new Pad { Content = "OFF", Lit = true, IsEnabled = false });
        rows.Children.Add(other);

        return Panel("gallery-pad", rows);
    }

    /// <summary>Shows the pressed state without a pointer, so it can be captured.</summary>
    static Pad HeldDown(Pad pad)
    {
        pad.Loaded += (_, _) => VisualStateManager.GoToState(pad, "Pressed", false);
        return pad;
    }

    static UIElement Toggles()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40 };
        row.Children.Add(new Toggle { Content = "ON" , IsChecked = true });
        row.Children.Add(new Toggle { Content = "OFF", IsChecked = false });
        row.Children.Add(new Toggle { Content = "LOCKED", IsEnabled = false });
        return Panel("gallery-toggle", row);
    }

    static UIElement Rotaries()
    {
        string[] formats = ["ORIGINAL", "FLAC", "WAV", "ALAC", "MP3", "AAC", "OPUS"];
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 56 };
        row.Children.Add(new Rotary { Label = "Output format", Items = formats, SelectedIndex = 0 });
        row.Children.Add(new Rotary { Label = "Output format", Items = formats, SelectedIndex = 3 });
        row.Children.Add(new Rotary { Label = "Output format", Items = formats, SelectedIndex = 6 });
        return Panel("gallery-rotary", row);
    }

    static UIElement Leds(bool hold)
    {
        var rows = new StackPanel { Spacing = 44 };
        foreach (var color in Enum.GetValues<LedColor>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 88 };
            row.Children.Add(new Led { Color = color, State = LedState.Off });
            row.Children.Add(new Led { Color = color, State = LedState.On });
            // The frame at the top of a strike flash, and the dim half of the busy blink.
            row.Children.Add(new Led { Color = color, FrozenBrightness = StrikeTimeline.PeakBrightness });
            row.Children.Add(hold
                ? new Led { Color = color, FrozenBrightness = StrikeTimeline.Busy[^1].Brightness }
                : new Led { Color = color, State = LedState.Strike });
            rows.Children.Add(row);
        }

        var page = new Grid { Padding = new Thickness(8) };
        page.Children.Add(new Faceplate { PanelName = "gallery-led", Padding = new Thickness(72, 56, 72, 56), Content = rows });
        return page;
    }

    static UIElement Bargraphs()
    {
        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(new Bargraph { Value = 0 });
        column.Children.Add(new Bargraph { Value = 50 });
        column.Children.Add(new Bargraph { Value = 100 });
        column.Children.Add(new Bargraph { Zones = BargraphZones.Vu, Value = 30 });
        column.Children.Add(new Bargraph { Zones = BargraphZones.Vu, Value = 70, Peak = 84 });
        column.Children.Add(new Bargraph { Zones = BargraphZones.Vu, Value = 95, Peak = 98 });

        var page = new Grid { Padding = new Thickness(8) };
        page.Children.Add(new Faceplate { PanelName = "gallery-bargraph", Content = column });
        return page;
    }

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
