using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class MotionTests
{
    [Theory]
    [InlineData(LedState.Strike)]
    [InlineData(LedState.Busy)]
    public void With_motion_reduced_a_moving_LED_holds_steady_at_full_brightness(LedState state)
    {
        Assert.Equal(1, StrikeTimeline.SteadyBrightness(state, reduceMotion: true));
    }

    [Theory]
    [InlineData(LedState.Strike)]
    [InlineData(LedState.Busy)]
    public void Otherwise_a_moving_LED_animates(LedState state)
    {
        Assert.Null(StrikeTimeline.SteadyBrightness(state, reduceMotion: false));
    }

    [Theory]
    [InlineData(LedState.Off, 0)]
    [InlineData(LedState.On, 1)]
    public void A_still_LED_shows_the_same_either_way(LedState state, double brightness)
    {
        Assert.Equal(brightness, StrikeTimeline.SteadyBrightness(state, reduceMotion: true));
        Assert.Equal(brightness, StrikeTimeline.SteadyBrightness(state, reduceMotion: false));
    }

    [Fact]
    public void The_pointer_light_is_centered_on_the_pointer()
    {
        var (x, y) = PointerLightLayout.OffsetFor(500, 300);
        Assert.Equal(500 - PointerLightLayout.Width / 2, x);
        Assert.Equal(300 - PointerLightLayout.Height / 2, y);
    }

    [Fact]
    public void The_pointer_light_stretches_along_the_horizontal_brushing()
    {
        Assert.True(PointerLightLayout.Width >= PointerLightLayout.Height * 2);
    }

    [Fact]
    public void The_pointer_light_stays_faint_and_is_stronger_on_bright_metal()
    {
        Assert.InRange(PointerLightLayout.PeakAlpha(LookMode.Dark), 1, 40);
        Assert.True(PointerLightLayout.PeakAlpha(LookMode.Light) > PointerLightLayout.PeakAlpha(LookMode.Dark));
    }
}
