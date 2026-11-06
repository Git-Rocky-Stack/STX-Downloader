using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// Type on the plate. Titles are cut in, with a catch of light on one lip and shadow on the other,
/// and run on into a milled line. Other kinds are silk-screened.
/// </summary>
public sealed partial class EngravedLabel : Grid
{
    readonly TextBlock _ink = NewText(), _high = NewText(), _low = NewText();
    readonly Grid _text = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Grid _lineLow = new() { Height = 1 }, _lineHigh = new() { Height = 1 };
    readonly StackPanel _line = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0) };
    LabelKind _kind = LabelKind.Label;
    string _value = "";

    public EngravedLabel()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());
        IsHitTestVisible = false;

        // The lips are decoration. Only the ink should be read aloud.
        AutomationProperties.SetAccessibilityView(_high, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(_low, AccessibilityView.Raw);
        _high.RenderTransform = new TranslateTransform { Y = 1 };
        _low.RenderTransform = new TranslateTransform { Y = -1 };
        _text.Children.Add(_high);
        _text.Children.Add(_low);
        _text.Children.Add(_ink);
        Children.Add(_text);

        _line.Children.Add(_lineLow);
        _line.Children.Add(_lineHigh);
        SetColumn(_line, 1);
        Children.Add(_line);

        Loaded += (_, _) => { LookState.Changed += Paint; Paint(); };
        Unloaded += (_, _) => LookState.Changed -= Paint;
        Paint();
    }

    public string Text
    {
        get => _value;
        set
        {
            _value = value ?? "";
            _ink.Text = _high.Text = _low.Text = _value.ToUpperInvariant();
        }
    }

    public LabelKind Kind
    {
        get => _kind;
        set { _kind = value; Paint(); }
    }

    /// <summary>A sentence that wraps within the width it's given instead of running off the plate. Off by default:
    /// labels and titles stay on one line.</summary>
    public bool Wraps
    {
        get => _ink.TextWrapping == TextWrapping.Wrap;
        set
        {
            foreach (var text in new[] { _ink, _high, _low }) text.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            // Wrapping needs a bounded width, which an Auto column never gives.
            ColumnDefinitions[0].Width = value ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            ColumnDefinitions[1].Width = value ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }
    }

    static TextBlock NewText() => new() { FontFamily = new FontFamily("Segoe UI"), TextWrapping = TextWrapping.NoWrap };

    void Paint()
    {
        var style = LabelStyles.For(_kind);
        var tokens = LookTokens.For(LookState.Mode);
        foreach (var text in new[] { _ink, _high, _low })
        {
            text.FontSize = style.Size;
            text.CharacterSpacing = style.Tracking;
            text.FontWeight = _kind == LabelKind.Title ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.SemiBold;
        }
        _ink.Foreground = LookBrushes.From(tokens[style.InkToken]);

        var lips = style.Engraved ? Visibility.Visible : Visibility.Collapsed;
        _high.Visibility = lips;
        _low.Visibility = lips;
        _line.Visibility = lips;
        _high.Foreground = LookBrushes.From(tokens["EngraveHigh"]);
        _low.Foreground = LookBrushes.From(tokens["EngraveLow"]);
        _lineLow.Background = LookBrushes.From(tokens["EdgeShadow"]);
        _lineHigh.Background = LookBrushes.From(tokens["EdgeHighlight"]);
    }
}
