using System.Diagnostics;
using System.Globalization;
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
        "pushswitch" => PushSwitches(),
        "rotary" => Rotaries(),
        "label" => Labels(),
        "list" => Lists(),
        "textbox" => TextBoxes(),
        "dialog" => Dialogs(),
        "states" => States(options.Hold),
        "all-primitives" => AllPrimitives(options.Hold),
        _ => new Grid(),
    };

    static UIElement Labels()
    {
        var column = new StackPanel { Spacing = 24 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Source truth" });
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Engine current" });
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Sub, Text = "Solid state media console" });
        return Panel("gallery-label", column);
    }

    static UIElement Lists()
    {
        var list = new DisplayList { Label = "Queue", Tone = DisplayTone.Cyan, Height = 240, ItemsSource = SampleRows, SelectedIndex = 1 };
        return Panel("gallery-list", new Display { Tone = DisplayTone.Cyan, Height = 276, Padding = new Thickness(4), Content = list });
    }

    static readonly string[] SampleRows = ["SAMPLE MUSIC VIDEO", "TECHNICAL WEBINAR RECORDING", "PRODUCT FILM 1080P", "LIVE SESSION OPUS", "PLAYLIST ENTRY 5"];

    static UIElement TextBoxes()
    {
        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(new Display
        {
            Tone = DisplayTone.Cyan,
            Padding = new Thickness(6),
            Content = new DisplayTextBox { Label = "Link", PlaceholderText = "PASTE LINK OR CAPTURE FROM BROWSER…", Tone = DisplayTone.Cyan },
        });
        column.Children.Add(new Display
        {
            Tone = DisplayTone.Amber,
            Padding = new Thickness(6),
            Content = new DisplayTextBox { Label = "Output folder", Text = "D:\\MEDIA\\MUSIC VIDEOS", Tone = DisplayTone.Amber },
        });
        return Panel("gallery-textbox", column);
    }

    static UIElement Dialogs()
    {
        var page = new Grid();
        page.Loaded += async (_, _) =>
        {
            var dialog = new EquipmentDialog { XamlRoot = page.XamlRoot };
            await dialog.ShowAsync("Replace file", "A file with this name is already in the output folder. Nothing is overwritten unless you say so.", "Save as new", "Cancel");
        };
        page.Children.Add(Panel("gallery-dialog", new Grid()));
        return page;
    }

    static UIElement States(bool hold)
    {
        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(new StateView { State = ViewState.Loading, Message = "READING SOURCE…", HoldStill = hold });
        column.Children.Add(new StateView { State = ViewState.Error, Message = "OUTPUT FOLDER IS GONE", ActionText = "RETRY" });
        column.Children.Add(new StateView { State = ViewState.Empty, Message = "NOTHING IN THE QUEUE" });
        column.Children.Add(new StateView { State = ViewState.Disabled, Message = "ENGINE NOT INSTALLED" });
        return Panel("gallery-states", column);
    }

    /// <summary>Everything at once, as the budgets are measured. Live unless held, when it must do no work at all.</summary>
    static UIElement AllPrimitives(bool hold)
    {
        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Source" });
        left.Children.Add(new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(6), Content = new DisplayTextBox { Label = "Link", PlaceholderText = "PASTE LINK…" } });
        left.Children.Add(new Display { Tone = DisplayTone.Amber, Height = 76, Content = Lines(DisplayTone.Amber, ("OPUS 160 KBPS · 48 KHZ", false), ("BIT-PERFECT", true)) });
        left.Children.Add(new Display { Tone = DisplayTone.Cyan, Height = 112, Padding = new Thickness(4), Content = new DisplayList { Label = "Queue", Height = 96, ItemsSource = SampleRows.Take(2).ToArray() } });
        left.Children.Add(new StateView { State = ViewState.Loading, Message = "READING SOURCE…", HoldStill = hold });

        var led = new Led { Color = LedColor.Amber };
        if (hold) led.FrozenBrightness = 1; else led.State = LedState.Strike;
        var progress = new Bargraph { Value = 62 };
        var vu = new Bargraph { Zones = BargraphZones.Vu, Value = 70, Peak = 84 };
        var readout = new SevenSegmentReadout { Text = "62" };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        top.Children.Add(led);
        top.Children.Add(readout);

        var pads = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pads.Children.Add(new Pad { Content = "RUN", Lit = true });
        pads.Children.Add(new Pad { Content = "PAUSE" });
        pads.Children.Add(new Pad { Content = "RETRY" });

        var hardware = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        hardware.Children.Add(new Rotary { Label = "Output format", Items = ["ORIGINAL", "FLAC", "WAV", "MP3"], SelectedIndex = 1 });
        hardware.Children.Add(new PushSwitch { Content = "NORMALIZE", IsChecked = true });
        hardware.Children.Add(new PushSwitch { Content = "TRAY" });

        var right = new StackPanel { Spacing = 12 };
        right.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Engine" });
        right.Children.Add(top);
        right.Children.Add(progress);
        right.Children.Add(vu);
        right.Children.Add(pads);
        right.Children.Add(hardware);

        var grid = new Grid { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        if (!hold)
        {
            // A meter moving at its real update rate, as during a job. Gallery only: this is not audio.
            var clock = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                double t = clock.Elapsed.TotalSeconds;
                vu.Value = 60 + 25 * Math.Sin(t * 2.1) * Math.Sin(t * 0.7);
                vu.Peak = vu.Value + 8;
                progress.Value = t * 4 % 100;
                readout.Text = ((int)(t * 4 % 100)).ToString(CultureInfo.InvariantCulture);
            };
            grid.Loaded += (_, _) => timer.Start();
            grid.Unloaded += (_, _) => timer.Stop();
        }

        var page = new Grid { Padding = new Thickness(8) };
        page.Children.Add(new Faceplate { PanelName = "gallery-all", Padding = new Thickness(32, 28, 32, 28), Content = grid });
        return page;
    }

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

    static UIElement PushSwitches()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40 };
        row.Children.Add(new PushSwitch { Content = "ON", IsChecked = true });
        row.Children.Add(new PushSwitch { Content = "OFF" });
        row.Children.Add(new PushSwitch { Content = "AMBER", IsChecked = true, LitColor = LedColor.Amber });
        row.Children.Add(new PushSwitch { Content = "LOCKED", IsEnabled = false });
        row.Children.Add(new PushSwitch { Content = "LOCKED ON", IsChecked = true, IsEnabled = false });
        return Panel("gallery-pushswitch", row);
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
