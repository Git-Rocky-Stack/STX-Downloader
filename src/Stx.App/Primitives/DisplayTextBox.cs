using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// An editable line inside a Display. It stays a real TextBox with new colors,
/// so editing, selection, clipboard and screen readers remain native.
/// </summary>
public sealed partial class DisplayTextBox : TextBox
{
    const double FieldHeight = 44;
    const string FocusRing = "#58C7FF";

    DisplayTone _tone = DisplayTone.Cyan;

    public DisplayTextBox()
    {
        DefaultStyleKey = typeof(TextBox);
        RequestedTheme = ElementTheme.Dark;
        MinHeight = FieldHeight;
        FontFamily = new FontFamily("Consolas");
        FontSize = 15;
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        VerticalContentAlignment = VerticalAlignment.Center;
        BorderThickness = new Thickness(1);
        Paint();
    }

    public DisplayTone Tone
    {
        get => _tone;
        set { _tone = value; Paint(); }
    }

    /// <summary>What this field is for, for screen readers. The placeholder alone goes away once you type.</summary>
    public string Label
    {
        get => AutomationProperties.GetName(this);
        set => AutomationProperties.SetName(this, value);
    }

    void Paint()
    {
        var text = LookBrushes.From(DisplayTones.Text(_tone, dimmed: false));
        var dim = LookBrushes.From(DisplayTones.Text(_tone, dimmed: true));
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var ring = LookBrushes.From(FocusRing);
        var selection = LookBrushes.Parse(DisplayTones.Text(_tone, dimmed: true));

        foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            Resources[$"TextControlForeground{state}"] = state == "Disabled" ? dim : text;
            Resources[$"TextControlPlaceholderForeground{state}"] = dim;
            Resources[$"TextControlBackground{state}"] = clear;
            Resources[$"TextControlBorderBrush{state}"] = state == "Focused" ? ring : clear;
        }
        Resources["TextControlSelectionHighlightColor"] = new SolidColorBrush(selection);
        PlaceholderForeground = dim;
    }
}
