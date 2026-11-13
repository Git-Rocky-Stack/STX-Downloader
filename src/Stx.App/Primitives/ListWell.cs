using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Stx.App.Primitives;

/// <summary>
/// The recessed tray a plate's list sits in: a shade darker than the metal, with a faint lip, on either plate's finish.
/// When the deck stretches a plate, the tray grows with it, so the extra height reads as list space.
/// </summary>
public static class ListTray
{
    public static Border Around(UIElement content) => new()
    {
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x2E, 0, 0, 0)),
        BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(6),
        Child = content,
    };
}

/// <summary>
/// Holds a list so it grows into the room its plate is given, and no taller than <see cref="Cap"/> when it's free to be
/// any height (in a scrolling rack). The list is always measured with a finite height, so it stays virtualized: only
/// the rows on screen are built, however tall it grows.
/// </summary>
public sealed partial class ListWell : Panel
{
    public double Cap { get; set; } = 420;

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = double.IsInfinity(availableSize.Height) ? Cap : Math.Max(0, availableSize.Height);
        var desired = new Size();
        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, height));
            desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width), Math.Max(desired.Height, child.DesiredSize.Height));
        }
        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
