using Stx.Look;
using Xunit;
using static Stx.Look.KnobRowLayout;

namespace Stx.Look.Tests;

public class KnobRowLayoutTests
{
    // Sizes as the Player measures them at 100%: the eight-digit time, the VOLUME knob, a three-position knob.
    static readonly Extent Time = new(140, 40), Volume = new(165, 170), Knob = new(140, 170);

    [Fact]
    public void Time_and_volume_sit_as_the_plates_grid_placed_them()
    {
        // The approved player screenshots: time at the left, VOLUME 12 in from the plate's inner edge.
        var plan = Arrange(448, 0, PlateLeadGap, [Time, Volume]);

        Assert.Equal(new Slot(0, 0, 140, 170), plan.Slots[0]);
        Assert.Equal(new Slot(448 - 12 - 165, 0, 165, 170), plan.Slots[1]);
        Assert.Equal(new Extent(140 + 24 + 165 + 12, 170), plan.Size);
    }

    [Fact]
    public void A_knob_that_fits_goes_beside_volume_at_the_right_edge()
    {
        var plan = Arrange(535, 0, PlateLeadGap, [Time, Volume, Knob]);

        Assert.Equal(new Slot(535 - 140, 0, 140, 170), plan.Slots[2]);
        Assert.Equal(535 - 140 - 12 - 165, plan.Slots[1].X);
        Assert.Equal(170, plan.Size.Height);
    }

    [Fact]
    public void One_line_stretches_to_the_height_it_is_given()
    {
        var plan = Arrange(535, 200, PlateLeadGap, [Time, Volume, Knob]);

        Assert.All(plan.Slots, s => Assert.Equal(200, s.Height));
    }

    [Fact]
    public void A_knob_that_does_not_fit_wraps_under_and_nothing_leaves_the_plate()
    {
        // The rack's right column at its narrowest, with ENHANCE on: the time had been squeezed to nothing.
        var plan = Arrange(448, 0, PlateLeadGap, [Time, Volume, Knob]);

        Assert.Equal(new Slot(0, 0, 140, 170), plan.Slots[0]);
        Assert.Equal(new Slot(448 - 165, 0, 165, 170), plan.Slots[1]);
        Assert.Equal(new Slot(448 - 140, 170 + LineGap, 140, 170), plan.Slots[2]);
        Assert.Equal(new Extent(448, 170 + LineGap + 170), plan.Size);
        AssertInside(plan, 448);
    }

    [Fact]
    public void Several_knobs_wrap_into_right_aligned_lines()
    {
        // ENHANCE, AUDIO, SUBTITLES and CHAPTER: two fit to a line (140 + 16 + 140 = 296; a third would need 452).
        var plan = Arrange(448, 0, PlateLeadGap, [Time, Volume, Knob, Knob, Knob, Knob]);

        double second = 170 + LineGap, third = second + 170 + LineGap;
        Assert.Equal(new Slot(448 - 296, second, 140, 170), plan.Slots[2]);
        Assert.Equal(new Slot(448 - 140, second, 140, 170), plan.Slots[3]);
        Assert.Equal(new Slot(448 - 296, third, 140, 170), plan.Slots[4]);
        Assert.Equal(new Slot(448 - 140, third, 140, 170), plan.Slots[5]);
        Assert.Equal(third + 170, plan.Size.Height);
        AssertInside(plan, 448);
    }

    [Fact]
    public void Too_narrow_for_time_and_volume_together_stacks_them()
    {
        var plan = Arrange(250, 0, PlateLeadGap, [Time, Volume]);

        Assert.Equal(new Slot(0, 0, 140, 40), plan.Slots[0]);
        Assert.Equal(new Slot(250 - 165, 40 + LineGap, 165, 170), plan.Slots[1]);
        Assert.Equal(40 + LineGap + 170, plan.Size.Height);
        AssertInside(plan, 250);
    }

    [Fact]
    public void Full_screen_is_one_line_packed_from_the_left_as_its_grid_columns_were()
    {
        // Unbounded, as the strip's Auto column measures it: time, 12, VOLUME, 12, the other knobs.
        var plan = Arrange(double.PositiveInfinity, 0, FullScreenLeadGap, [Time, Volume, Knob]);

        Assert.Equal(0, plan.Slots[0].X);
        Assert.Equal(140 + 12, plan.Slots[1].X);
        Assert.Equal(140 + 12 + 165 + 12, plan.Slots[2].X);
        Assert.Equal(new Extent(140 + 12 + 165 + 12 + 140, 170), plan.Size);
    }

    [Fact]
    public void A_knob_not_on_show_takes_no_room()
    {
        var with = Arrange(535, 0, PlateLeadGap, [Time, Volume, new Extent(0, 0), Knob]);
        var without = Arrange(535, 0, PlateLeadGap, [Time, Volume, Knob]);

        Assert.Equal(without.Slots[2], with.Slots[3]);
        Assert.Equal(without.Size, with.Size);
    }

    static void AssertInside(Plan plan, double width) =>
        Assert.All(plan.Slots, s => Assert.True(s.X >= 0 && s.X + s.Width <= width, $"{s} leaves a {width} wide plate"));
}
