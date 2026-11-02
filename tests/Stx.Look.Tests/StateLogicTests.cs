using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class StateLogicTests
{
    [Fact]
    public void Loading_blinks_an_amber_led() =>
        Assert.Equal((LedColor.Amber, LedState.Busy), Led(ViewState.Loading));

    [Fact]
    public void Error_shows_a_steady_red_led() =>
        Assert.Equal((LedColor.Red, LedState.On), Led(ViewState.Error));

    [Theory]
    [InlineData(ViewState.Empty)]
    [InlineData(ViewState.Disabled)]
    public void Idle_states_leave_the_led_off(ViewState state) =>
        Assert.Equal(LedState.Off, StateLook.For(state).Led);

    [Fact]
    public void Only_an_error_offers_a_way_forward()
    {
        Assert.True(StateLook.For(ViewState.Error).OffersAction);
        Assert.False(StateLook.For(ViewState.Loading).OffersAction);
        Assert.False(StateLook.For(ViewState.Empty).OffersAction);
        Assert.False(StateLook.For(ViewState.Disabled).OffersAction);
    }

    [Theory]
    [InlineData(ViewState.Empty)]
    [InlineData(ViewState.Disabled)]
    public void Idle_states_use_dimmed_text(ViewState state) =>
        Assert.True(StateLook.For(state).Dimmed);

    [Theory]
    [InlineData(ViewState.Loading)]
    [InlineData(ViewState.Error)]
    public void Live_states_use_bright_text(ViewState state) =>
        Assert.False(StateLook.For(state).Dimmed);

    static (LedColor, LedState) Led(ViewState state) => (StateLook.For(state).Color, StateLook.For(state).Led);

    [Theory]
    [InlineData(DisplayTone.Amber)]
    [InlineData(DisplayTone.Cyan)]
    public void Dimmed_display_text_meets_aa_on_its_screen(DisplayTone tone) =>
        Assert.True(LookTokens.Contrast(DisplayTones.Text(tone, true), DisplayTones.ScreenCenter(tone)) >= 4.5);

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void Both_modes_supply_engraving_tokens(LookMode mode)
    {
        Assert.Contains("EngraveHigh", LookTokens.RequiredKeys);
        Assert.Contains("EngraveLow", LookTokens.RequiredKeys);
        Assert.Equal(9, LookTokens.For(mode)["EngraveHigh"].Length);
        Assert.Equal(9, LookTokens.For(mode)["EngraveLow"].Length);
    }

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void Dim_ink_meets_aa_on_every_plate_stop(LookMode mode)
    {
        var t = LookTokens.For(mode);
        foreach (string stop in new[] { "PlateTop", "PlateMid", "PlateBottom" })
            Assert.True(LookTokens.Contrast(t["InkDim"], t[stop]) >= 4.5, $"{mode} InkDim on {stop}");
    }

    [Theory]
    [InlineData(LabelKind.Title, "Ink", 11.5, 240)]
    [InlineData(LabelKind.Label, "InkDim", 10.5, 160)]
    [InlineData(LabelKind.Sub, "InkDim", 9.5, 300)]
    public void Label_kinds_match_the_approved_mockup(LabelKind kind, string ink, double size, int tracking)
    {
        var style = LabelStyles.For(kind);
        Assert.Equal(ink, style.InkToken);
        Assert.Equal(size, style.Size);
        Assert.Equal(tracking, style.Tracking);
    }

    [Fact]
    public void Only_titles_are_engraved()
    {
        Assert.True(LabelStyles.For(LabelKind.Title).Engraved);
        Assert.False(LabelStyles.For(LabelKind.Label).Engraved);
    }
}
