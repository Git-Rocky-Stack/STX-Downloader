namespace Stx.Look;

/// <summary>
/// Where the Player's time readout, VOLUME knob and its other knobs (ENHANCE, and AUDIO, SUBTITLES or CHAPTER when a file
/// has a choice) sit. When they fit on one line: the time at the left, the knobs at the right, VOLUME 12 in from the
/// others, as the plate's grid always placed them, so the approved screenshots still hold. When they don't, the other
/// knobs wrap onto lines of their own under it, right-aligned, so nothing is pushed off the plate (the time readout and
/// the FULL pad were, with ENHANCE on in the rack's right column, 2026-11-09).
/// </summary>
public static class KnobRowLayout
{
    public readonly record struct Extent(double Width, double Height);

    public readonly record struct Slot(double X, double Y, double Width, double Height);

    /// <summary>A slot for each item, in order, and the size the whole takes.</summary>
    public sealed record Plan(IReadOnlyList<Slot> Slots, Extent Size);

    /// <summary>Time to VOLUME on the plate: the grid's two 12 px column gaps either side of an empty column.</summary>
    public const double PlateLeadGap = 24;

    /// <summary>Time to VOLUME in full screen's one-line strip: one column gap.</summary>
    public const double FullScreenLeadGap = 12;

    public const double VolumeGap = 12, KnobGap = 16, LineGap = 8;

    /// <param name="width">The width to fit in, or infinity for one line whatever its length (full screen's strip).</param>
    /// <param name="height">The height being arranged, which one line stretches to; 0 when measuring.</param>
    /// <param name="leadGap">The space between the time and VOLUME.</param>
    /// <param name="items">The time, VOLUME, then each other knob, each as it measures on its own.</param>
    public static Plan Arrange(double width, double height, double leadGap, IReadOnlyList<Extent> items)
    {
        if (items.Count < 2)
            return new([.. items.Select(e => new Slot(0, 0, e.Width, e.Height))], items.Count == 0 ? default : items[0]);

        Extent lead = items[0], volume = items[1];
        var slots = new Slot[items.Count];
        // A knob not on show measures zero wide; it takes no slot and no gap.
        var knobs = Enumerable.Range(2, items.Count - 2).Where(i => items[i].Width > 0).ToList();
        double knobsWidth = RowWidth(knobs.Select(i => items[i].Width));
        double oneLine = lead.Width + leadGap + volume.Width + VolumeGap + knobsWidth;

        if (oneLine <= width)
        {
            double line = Math.Max(height, items.Max(e => e.Height));
            double right = double.IsPositiveInfinity(width) ? oneLine : width;
            slots[0] = new(0, 0, lead.Width, line);
            slots[1] = new(right - knobsWidth - VolumeGap - volume.Width, 0, volume.Width, line);
            double x = right - knobsWidth;
            foreach (int i in knobs)
            {
                slots[i] = new(x, 0, items[i].Width, line);
                x += items[i].Width + KnobGap;
            }
            return new(slots, new(oneLine, line));
        }

        // Too long for one line: the time and VOLUME first (stacked if even they don't fit), then the knobs in lines.
        double y;
        if (lead.Width + leadGap + volume.Width <= width)
        {
            double line = Math.Max(lead.Height, volume.Height);
            slots[0] = new(0, 0, lead.Width, line);
            slots[1] = new(Math.Max(0, width - volume.Width), 0, volume.Width, line);
            y = line;
        }
        else
        {
            slots[0] = new(0, 0, lead.Width, lead.Height);
            slots[1] = new(Math.Max(0, width - volume.Width), lead.Height + LineGap, volume.Width, volume.Height);
            y = lead.Height + LineGap + volume.Height;
        }

        for (int start = 0; start < knobs.Count;)
        {
            // Each line takes as many knobs as fit, and always at least one.
            int end = start + 1;
            while (end < knobs.Count && RowWidth(knobs[start..(end + 1)].Select(i => items[i].Width)) <= width) end++;
            var line = knobs[start..end];
            double lineHeight = line.Max(i => items[i].Height);
            double x = Math.Max(0, width - RowWidth(line.Select(i => items[i].Width)));
            y += LineGap;
            foreach (int i in line)
            {
                slots[i] = new(x, y, items[i].Width, lineHeight);
                x += items[i].Width + KnobGap;
            }
            y += lineHeight;
            start = end;
        }
        return new(slots, new(width, y));
    }

    static double RowWidth(IEnumerable<double> widths)
    {
        double total = 0;
        int count = 0;
        foreach (double w in widths) { total += w; count++; }
        return count == 0 ? 0 : total + (count - 1) * KnobGap;
    }
}
