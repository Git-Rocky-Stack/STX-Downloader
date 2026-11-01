using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class BargraphLogicTests
{
    [Theory]
    [InlineData(50, 30, 15)]
    [InlineData(-5, 30, 0)]
    [InlineData(120, 30, 30)]
    [InlineData(0, 30, 0)]
    [InlineData(100, 30, 30)]
    [InlineData(double.NaN, 30, 0)]
    [InlineData(double.PositiveInfinity, 30, 0)]
    public void Lit_count_follows_value(double value, int segments, int expected) =>
        Assert.Equal(expected, BargraphLogic.LitCount(value, segments));

    [Theory]
    [InlineData(84, 30, 24)]
    [InlineData(100, 30, 29)]
    [InlineData(120, 30, 29)]
    [InlineData(0, 30, -1)]
    [InlineData(-3, 30, -1)]
    [InlineData(double.NaN, 30, -1)]
    public void Peak_index_marks_one_segment_or_none(double peak, int segments, int expected) =>
        Assert.Equal(expected, BargraphLogic.PeakIndex(peak, segments));

    [Theory]
    [InlineData(71, LedColor.Green)]  // Segment 72 of 100 ends exactly at 72%.
    [InlineData(72, LedColor.Amber)]
    [InlineData(89, LedColor.Amber)]  // Segment 90 of 100 ends exactly at 90%.
    [InlineData(90, LedColor.Red)]
    [InlineData(99, LedColor.Red)]
    public void Vu_zones_are_green_to_72_amber_to_90_red_above(int index, LedColor expected) =>
        Assert.Equal(expected, BargraphLogic.ZoneColor(index, 100, BargraphZones.Vu));

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Progress_meters_stay_green(int index) =>
        Assert.Equal(LedColor.Green, BargraphLogic.ZoneColor(index, 30, BargraphZones.Progress));

    [Fact]
    public void Only_segments_that_changed_are_touched_when_the_value_rises() =>
        Assert.Equal([15, 16], BargraphLogic.Changed(15, 17, -1, -1));

    [Fact]
    public void Only_segments_that_changed_are_touched_when_the_value_falls() =>
        Assert.Equal([15, 16], BargraphLogic.Changed(17, 15, -1, -1));

    [Fact]
    public void Nothing_is_touched_when_nothing_changed() =>
        Assert.Empty(BargraphLogic.Changed(15, 15, 20, 20));

    [Fact]
    public void A_moving_peak_touches_its_old_and_new_segments() =>
        Assert.Equal([20, 22], BargraphLogic.Changed(15, 15, 20, 22));

    [Fact]
    public void A_segment_that_changed_twice_over_is_touched_once() =>
        Assert.Equal([15, 16, 18], BargraphLogic.Changed(15, 17, 16, 18));
}
