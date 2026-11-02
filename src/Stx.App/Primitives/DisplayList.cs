using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// A scrolling list for use inside a Display. It stays a real ListView with new colors,
/// so scrolling, selection, keyboard and screen readers remain native.
/// </summary>
public sealed partial class DisplayList : ListView
{
    const double RowHeight = 44;

    DisplayTone _tone = DisplayTone.Cyan;

    public DisplayList()
    {
        DefaultStyleKey = typeof(ListView);
        RequestedTheme = ElementTheme.Dark;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Paint();
    }

    public DisplayTone Tone
    {
        get => _tone;
        set { _tone = value; Paint(); }
    }

    /// <summary>What the list holds, for screen readers.</summary>
    public string Label
    {
        get => AutomationProperties.GetName(this);
        set => AutomationProperties.SetName(this, value);
    }

    void Paint()
    {
        var text = LookBrushes.Parse(DisplayTones.Text(_tone, dimmed: false));
        var wash = new SolidColorBrush(text) { Opacity = 0.16 };
        var hover = new SolidColorBrush(text) { Opacity = 0.09 };
        var brush = new SolidColorBrush(text);
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        Resources["ListViewItemForeground"] = brush;
        Resources["ListViewItemForegroundPointerOver"] = brush;
        Resources["ListViewItemForegroundPressed"] = brush;
        Resources["ListViewItemForegroundSelected"] = brush;
        Resources["ListViewItemForegroundSelectedPointerOver"] = brush;
        Resources["ListViewItemForegroundSelectedPressed"] = brush;
        Resources["ListViewItemBackground"] = clear;
        Resources["ListViewItemBackgroundPointerOver"] = hover;
        Resources["ListViewItemBackgroundPressed"] = wash;
        Resources["ListViewItemBackgroundSelected"] = wash;
        Resources["ListViewItemBackgroundSelectedPointerOver"] = wash;
        Resources["ListViewItemBackgroundSelectedPressed"] = wash;
        Resources["ListViewItemSelectionIndicatorBrush"] = brush;

        var rows = new Style(typeof(ListViewItem));
        rows.Setters.Add(new Setter(MinHeightProperty, RowHeight));
        rows.Setters.Add(new Setter(FontFamilyProperty, new FontFamily("Consolas")));
        rows.Setters.Add(new Setter(FontSizeProperty, 15.0));
        rows.Setters.Add(new Setter(FontWeightProperty, Microsoft.UI.Text.FontWeights.SemiBold));
        ItemContainerStyle = rows;
    }
}
