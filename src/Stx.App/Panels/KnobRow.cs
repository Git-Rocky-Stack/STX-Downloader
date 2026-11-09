using Microsoft.UI.Xaml.Controls;
using Stx.Look;
using Windows.Foundation;

namespace Stx.App.Panels;

/// <summary>
/// The Player's time readout, VOLUME knob and other knobs, in that order, placed by <see cref="KnobRowLayout"/>: one line
/// when they fit, the other knobs wrapping under it when they don't. Each child is placed in its slot and aligns itself
/// there, so the time stays centred against the knobs.
/// </summary>
internal sealed partial class KnobRow : Panel
{
    double _leadGap = KnobRowLayout.PlateLeadGap;

    /// <summary>The space between the time and VOLUME.</summary>
    public double LeadGap
    {
        get => _leadGap;
        set
        {
            if (value == _leadGap) return;
            _leadGap = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(unbounded);
        var size = Plan(availableSize.Width, 0).Size;
        return new Size(size.Width, size.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slots = Plan(finalSize.Width, finalSize.Height).Slots;
        for (int i = 0; i < Children.Count; i++) Children[i].Arrange(new Rect(slots[i].X, slots[i].Y, slots[i].Width, slots[i].Height));
        return finalSize;
    }

    KnobRowLayout.Plan Plan(double width, double height) =>
        KnobRowLayout.Arrange(width, height, LeadGap, [.. Children.Select(c => new KnobRowLayout.Extent(c.DesiredSize.Width, c.DesiredSize.Height))]);
}
