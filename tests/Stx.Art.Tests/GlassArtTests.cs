using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.Art.Tests;

public class GlassArtTests
{
    [Fact]
    public void Glass_reflection_sits_at_the_top_left()
    {
        var g = GlassArt.Render(400, 100);
        Assert.True(g.Alpha(10, 10) > g.Alpha(300, 60), "reflection should be strongest top left");
    }

    [Fact]
    public void Glass_leaves_the_middle_of_the_screen_readable()
    {
        var g = GlassArt.Render(400, 100);
        Assert.True(g.Alpha(200, 70) < 30, "glass must not wash out text");
    }

    [Fact]
    public void Glass_over_left_aligned_text_stays_subtle()
    {
        // Display text starts at the left, so the reflection there must not wash it out.
        var g = GlassArt.Render(800, 100);
        Assert.True(g.Alpha(20, 40) <= 26, $"alpha was {g.Alpha(20, 40)}");
    }

    [Fact]
    public void Screen_has_a_pixel_grid()
    {
        var s = ScreenArt.Render(64, 64, DisplayTone.Amber);
        Assert.NotEqual(s.Luma(32, 32), s.Luma(33, 33));
    }

    [Theory]
    [InlineData(DisplayTone.Amber)]
    [InlineData(DisplayTone.Cyan)]
    public void Screen_is_recessed_so_edges_are_darker_than_center(DisplayTone tone)
    {
        var s = ScreenArt.Render(200, 80, tone);
        Assert.True(s.Luma(1, 1) < s.Luma(100, 40));
    }

    [Fact]
    public void Tones_produce_different_screens() =>
        Assert.NotEqual(ScreenArt.Render(32, 32, DisplayTone.Amber).Hash(), ScreenArt.Render(32, 32, DisplayTone.Cyan).Hash());
}
