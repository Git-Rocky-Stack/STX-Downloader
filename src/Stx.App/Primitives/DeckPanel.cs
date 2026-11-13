using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Stx.App.Primitives;

/// <summary>
/// The console's deck: two columns side by side whose bottoms always meet, so the rack reads as one block with no
/// dead space under the shorter column, however full or empty its plates are. Each column is measured at its own
/// height, then both are measured again at the taller one's, so a column whose last plate stretches (a Grid with a star
/// row) grows that plate to fill. A collapsed left column (full screen) gives the right one the whole width.
/// </summary>
public sealed partial class DeckPanel : Panel
{
    public double LeftWeight { get; set; } = 1.2;
    public double RightWeight { get; set; } = 1;
    public double Spacing { get; set; } = 8;

    /// <summary>
    /// A deck column: its first plate at its own height, the second taking whatever height the deck gives the column, so
    /// the column's bottom meets the other's.
    /// </summary>
    public static Grid Column(FrameworkElement top, FrameworkElement fill)
    {
        var column = new Grid { RowSpacing = 8 };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        fill.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetRow(fill, 1);
        column.Children.Add(top);
        column.Children.Add(fill);
        return column;
    }

    UIElement? Left => Children.Count > 0 ? Children[0] : null;
    UIElement? Right => Children.Count > 1 ? Children[1] : null;

    (double Left, double Gap, double Right) Widths(double width)
    {
        if (Left is not { Visibility: Visibility.Visible }) return (0, 0, width);
        double room = Math.Max(0, width - Spacing);
        double left = room * LeftWeight / (LeftWeight + RightWeight);
        return (left, Spacing, room - left);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Left is not { } left || Right is not { } right) return new Size();
        var (lw, _, rw) = Widths(double.IsInfinity(availableSize.Width) ? 1360 : availableSize.Width);
        if (left.Visibility != Visibility.Visible)
        {
            right.Measure(new Size(rw, availableSize.Height));
            return new Size(rw, right.DesiredSize.Height);
        }

        var free = double.PositiveInfinity;
        left.Measure(new Size(lw, free));
        right.Measure(new Size(rw, free));
        double height = Math.Max(left.DesiredSize.Height, right.DesiredSize.Height);
        // Again at the shared height: the shorter column's stretching plate takes the rest.
        left.Measure(new Size(lw, height));
        right.Measure(new Size(rw, height));
        return new Size(double.IsInfinity(availableSize.Width) ? lw + Spacing + rw : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Left is not { } left || Right is not { } right) return finalSize;
        var (lw, gap, rw) = Widths(finalSize.Width);
        left.Arrange(new Rect(0, 0, lw, finalSize.Height));
        right.Arrange(new Rect(lw + gap, 0, rw, finalSize.Height));
        return finalSize;
    }
}
