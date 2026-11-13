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
    public void Progress_meters_never_warn(int index) =>
        Assert.Equal(LedColor.Blue, BargraphLogic.ZoneColor(index, 30, BargraphZones.Progress));

    [Fact]
    public void A_progress_meter_sweeps_from_aqua_to_violet()
    {
        Assert.Equal("#22E6B4", BargraphLogic.ZoneHex(0, 30, BargraphZones.Progress));
        Assert.Equal("#B06BFF", BargraphLogic.ZoneHex(29, 30, BargraphZones.Progress));
        // No step is amber or red: blue is always strong, and red never leads both others.
        for (int i = 0; i < 30; i++)
        {
            var (r, g, b) = Rgb(BargraphLogic.ZoneHex(i, 30, BargraphZones.Progress));
            Assert.True(b >= 150, $"segment {i} is {r},{g},{b}");
            Assert.True(r < g || r < b, $"segment {i} is {r},{g},{b}");
        }
    }

    [Fact]
    public void A_progress_meter_changes_color_gradually()
    {
        for (int i = 1; i < 30; i++)
        {
            var (r1, g1, b1) = Rgb(BargraphLogic.ZoneHex(i - 1, 30, BargraphZones.Progress));
            var (r2, g2, b2) = Rgb(BargraphLogic.ZoneHex(i, 30, BargraphZones.Progress));
            Assert.True(Math.Abs(r1 - r2) + Math.Abs(g1 - g2) + Math.Abs(b1 - b2) <= 90, $"a jump between segments {i - 1} and {i}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(100)]
    public void A_ramp_is_cut_into_a_few_shared_colors(int segments)
    {
        foreach (var zones in new[] { BargraphZones.Progress, BargraphZones.Vu })
        {
            int colors = Enumerable.Range(0, segments).Select(i => BargraphLogic.ZoneHex(i, segments, zones)).Distinct().Count();
            Assert.InRange(colors, 1, BargraphLogic.RampSteps * 3);
        }
    }

    [Fact]
    public void Each_level_segment_is_drawn_in_its_zones_family()
    {
        for (int i = 0; i < 100; i++)
        {
            var (r, g, b) = Rgb(BargraphLogic.ZoneHex(i, 100, BargraphZones.Vu));
            switch (BargraphLogic.ZoneColor(i, 100, BargraphZones.Vu))
            {
                case LedColor.Green: Assert.True(g > r && g > b, $"segment {i} should read green: {r},{g},{b}"); break;
                case LedColor.Amber: Assert.True(r > b && g > b && r >= g, $"segment {i} should read amber: {r},{g},{b}"); break;
                default: Assert.True(r > g * 2 && r > b, $"segment {i} should read red: {r},{g},{b}"); break;
            }
        }
    }

    static (int R, int G, int B) Rgb(string hex)
    {
        Assert.Matches("^#[0-9A-F]{6}$", hex);
        return (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));
    }

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
