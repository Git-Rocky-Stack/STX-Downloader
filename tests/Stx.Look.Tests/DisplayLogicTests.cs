using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class DisplayLogicTests
{
    [Fact]
    public void Glow_text_redraws_only_on_text_change()
    {
        var gate = new GlowRedrawGate();
        Assert.True(gate.ShouldRedraw("BIT-PERFECT", 100, 20));
        Assert.False(gate.ShouldRedraw("BIT-PERFECT", 100, 20));
        Assert.False(gate.ShouldRedraw("BIT-PERFECT", 100, 20));
        Assert.Equal(1, gate.DrawCount);
    }

    [Fact]
    public void New_text_redraws()
    {
        var gate = new GlowRedrawGate();
        gate.ShouldRedraw("A", 10, 20);
        Assert.True(gate.ShouldRedraw("B", 10, 20));
        Assert.Equal(2, gate.DrawCount);
    }

    [Fact]
    public void Resized_text_redraws_because_the_mask_must_match_the_text_box()
    {
        var gate = new GlowRedrawGate();
        gate.ShouldRedraw("A", 10, 20);
        Assert.True(gate.ShouldRedraw("A", 12, 20));
    }

    [Fact]
    public void Nothing_to_draw_before_layout()
    {
        var gate = new GlowRedrawGate();
        Assert.False(gate.ShouldRedraw("A", 0, 0));
        Assert.Equal(0, gate.DrawCount);
    }

    [Fact]
    public void Glow_layers_run_from_tight_core_to_wide_halo()
    {
        var layers = DisplayTones.GlowBlurs;
        Assert.True(layers.Count >= 3);
        Assert.True(layers[0] < 4, "tight core");
        Assert.True(layers[^1] > 15, "wide halo");
    }

    [Fact]
    public void Glow_core_is_softer_than_the_text_so_letters_stay_crisp()
    {
        Assert.True(DisplayTones.GlowOpacities[0] <= 0.6f);
        Assert.Equal(DisplayTones.GlowBlurs.Count, DisplayTones.GlowOpacities.Count);
    }

    [Theory]
    [InlineData(DisplayTone.Amber, "#FFC865", "#BA8730")]
    [InlineData(DisplayTone.Cyan, "#8AE8FF", "#40A6C8")]
    public void Tones_use_the_approved_colors(DisplayTone tone, string bright, string dim)
    {
        Assert.Equal(bright, DisplayTones.Text(tone, dimmed: false));
        Assert.Equal(dim, DisplayTones.Text(tone, dimmed: true));
    }

    [Theory]
    [InlineData(DisplayTone.Amber)]
    [InlineData(DisplayTone.Cyan)]
    public void Bright_display_text_meets_aa_on_its_screen(DisplayTone tone) =>
        Assert.True(LookTokens.Contrast(DisplayTones.Text(tone, false), DisplayTones.ScreenCenter(tone)) >= 4.5);
}
