using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class LedLogicTests
{
    static int RisingFlashes(IReadOnlyList<(double T, double Brightness)> timeline)
    {
        int flashes = 0;
        for (int i = 1; i < timeline.Count; i++)
            if (timeline[i - 1].Brightness < 1.5 && timeline[i].Brightness >= 1.5) flashes++;
        return flashes;
    }

    [Fact]
    public void Strike_flashes_at_least_three_times_before_settling() =>
        Assert.True(RisingFlashes(StrikeTimeline.Default) >= 3);

    [Fact]
    public void Strike_ends_within_1_2_seconds() =>
        Assert.InRange(StrikeTimeline.Default[^1].T, 0.1, 1.2);

    [Fact]
    public void Strike_settles_at_steady_brightness() =>
        Assert.Equal(1, StrikeTimeline.Default[^1].Brightness);

    [Fact]
    public void Strike_only_moves_forward_through_brightness_values_it_can_draw()
    {
        var timeline = StrikeTimeline.Default;
        Assert.Equal(0, timeline[0].T);
        for (int i = 1; i < timeline.Count; i++) Assert.True(timeline[i].T > timeline[i - 1].T);
        Assert.All(timeline, k => Assert.InRange(k.Brightness, 0, StrikeTimeline.PeakBrightness));
    }

    [Fact]
    public void Busy_blinks_between_bright_and_dim_without_fading()
    {
        var busy = StrikeTimeline.Busy;
        Assert.Equal(1.12, busy[0].Brightness);
        Assert.Equal(0.34, busy[^1].Brightness);
        Assert.Equal(StrikeTimeline.BusySeconds, busy[^1].T);
        double fall = busy[2].T - busy[1].T;
        Assert.True(fall < 0.02, "the blink is a step, not a fade");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0.5, 0.5, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(StrikeTimeline.PeakBrightness, 1, 1)]
    public void Brightness_splits_into_lit_and_flare_layers(double brightness, double lit, double flare)
    {
        var layers = StrikeTimeline.Split(brightness);
        Assert.Equal(lit, layers.Lit, 3);
        Assert.Equal(flare, layers.Flare, 3);
    }

    [Fact]
    public void Brightness_above_the_peak_does_not_overdrive_the_flare() =>
        Assert.Equal(1, StrikeTimeline.Split(99).Flare);

    [Fact]
    public void Strike_hands_over_to_busy() =>
        Assert.Equal(LedState.Busy, StrikeTimeline.After(LedState.Strike));

    [Theory]
    [InlineData(LedState.Off)]
    [InlineData(LedState.On)]
    [InlineData(LedState.Busy)]
    public void Other_states_stay_as_they_are(LedState state) =>
        Assert.Equal(state, StrikeTimeline.After(state));

    [Theory]
    [InlineData(LedColor.Green, "#3BFF7A", "#0E7A35")]
    [InlineData(LedColor.Red, "#FF4242", "#7A0E0E")]
    [InlineData(LedColor.Amber, "#FFB020", "#7F4B00")]
    [InlineData(LedColor.Blue, "#49C8FF", "#0B5578")]
    public void Colors_match_the_approved_mockup(LedColor color, string core, string deep)
    {
        Assert.Equal(core, LedColors.Core(color));
        Assert.Equal(deep, LedColors.Deep(color));
    }
}
