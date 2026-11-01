using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// A modal panel bolted over the console. It stays a real ContentDialog, so focus trapping, Escape
/// and the dimmed backdrop remain native, and its face is a faceplate with pads.
/// Set XamlRoot before showing it.
/// </summary>
public sealed partial class EquipmentDialog : ContentDialog
{
    const double PanelWidth = 460;

    ContentDialogResult _result = ContentDialogResult.None;

    public EquipmentDialog()
    {
        DefaultStyleKey = typeof(ContentDialog);
        if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) && style is Style dialogStyle)
            Style = dialogStyle;

        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Resources["ContentDialogBackground"] = clear;
        Resources["ContentDialogBorderBrush"] = clear;
        Resources["ContentDialogTopOverlay"] = clear;
        Resources["ContentDialogSeparatorBorderBrush"] = clear;
        Resources["ContentDialogBorderWidth"] = new Thickness(0);
        Resources["ContentDialogPadding"] = new Thickness(0);
        Resources["ContentDialogMaxWidth"] = PanelWidth + 40;
    }

    /// <summary>Returns Primary or Secondary for the pad pressed, and None if dismissed with Escape.</summary>
    public async Task<ContentDialogResult> ShowAsync(string title, string body, string primary, string? secondary)
    {
        _result = ContentDialogResult.None;
        Content = BuildPanel(title, body, primary, secondary);
        await ShowAsync();
        return _result;
    }

    UIElement BuildPanel(string title, string body, string primary, string? secondary)
    {
        var column = new StackPanel { Spacing = 16 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = title });
        column.Children.Add(new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            LineHeight = 20,
            Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]),
        });

        var pads = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Right };
        if (secondary is not null) pads.Children.Add(Button(secondary, ContentDialogResult.Secondary, lit: false));
        pads.Children.Add(Button(primary, ContentDialogResult.Primary, lit: true));
        column.Children.Add(pads);

        return new Faceplate { PanelName = "dialog", Width = PanelWidth, Padding = new Thickness(36, 32, 36, 32), Content = column };
    }

    Pad Button(string text, ContentDialogResult result, bool lit)
    {
        var pad = new Pad { Content = text.ToUpperInvariant(), Lit = lit };
        pad.Click += (_, _) =>
        {
            _result = result;
            Hide();
        };
        return pad;
    }
}
