using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Stx.App.Primitives;

/// <summary>
/// A plate's row of pads that wraps onto a second line when the plate is too narrow for them all, instead of letting
/// the last ones run off its edge. Collapsed pads take no room and no spacing.
/// </summary>
public sealed partial class PadRow : Panel
{
    /// <summary>Between pads on a line, and between lines. On the 8-point grid's 4-point step, as the rows always were.</summary>
    public double Spacing { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Lay(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Lay(finalSize.Width, arrange: true);
        return finalSize;
    }

    Size Lay(double width, bool arrange)
    {
        double x = 0, y = 0, line = 0, widest = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (child.Visibility == Visibility.Collapsed || size.Width <= 0)
            {
                if (arrange) child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            if (x > 0 && x + size.Width > width)
            {
                y += line + Spacing;
                x = line = 0;
            }
            if (arrange) child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            line = Math.Max(line, size.Height);
            widest = Math.Max(widest, x - Spacing);
        }
        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y + line);
    }
}
