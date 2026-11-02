using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class MarqueeTimelineTests
{
    [Theory]
    [InlineData(100, true, false, false, true)]
    [InlineData(100, false, true, false, true)]
    [InlineData(100, false, false, false, false)]
    [InlineData(100, true, true, true, false)]
    [InlineData(3, true, true, false, false)]
    [InlineData(-50, true, true, false, false)]
    public void Only_a_line_too_long_for_its_screen_moves_and_only_when_it_is_wanted(double overflow, bool working, bool lookedAt, bool reduceMotion, bool expected) =>
        Assert.Equal(expected, MarqueeTimeline.ShouldScroll(overflow, working, lookedAt, reduceMotion));

    [Fact]
    public void A_longer_line_takes_longer_at_the_same_speed()
    {
        Assert.Equal(MarqueeTimeline.HoldSeconds * 2 + 1, MarqueeTimeline.PassSeconds(MarqueeTimeline.PixelsPerSecond), 6);
        Assert.Equal(MarqueeTimeline.HoldSeconds * 2 + 10, MarqueeTimeline.PassSeconds(MarqueeTimeline.PixelsPerSecond * 10), 6);
    }

    [Fact]
    public void A_pass_holds_at_both_ends_for_the_same_time()
    {
        var (starts, ends) = MarqueeTimeline.Fractions(400);
        double pass = MarqueeTimeline.PassSeconds(400);
        Assert.Equal(MarqueeTimeline.HoldSeconds, starts * pass, 6);
        Assert.Equal(MarqueeTimeline.HoldSeconds, (1 - ends) * pass, 6);
        Assert.True(starts < ends);
    }

    [Fact]
    public void A_row_only_looked_at_comes_to_rest() => Assert.InRange(MarqueeTimeline.PassesWhileLookedAt, 1, 5);
}
