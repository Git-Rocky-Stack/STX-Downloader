using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class FaceplateLogicTests
{
    [Fact]
    public void Screws_on_every_corner_even_small_panels()
    {
        var s = ScrewLayout.For(60, 40);
        Assert.Equal(4, s.Count);
        Assert.Contains(s, p => p.X == 6 && p.Y == 6);
        Assert.Contains(s, p => p.X == 60 - 24 && p.Y == 6);
        Assert.Contains(s, p => p.X == 6 && p.Y == 40 - 24);
        Assert.Contains(s, p => p.X == 60 - 24 && p.Y == 40 - 24);
    }

    [Fact]
    public void Screw_angles_are_stable_for_a_panel_name()
    {
        var a = ScrewLayout.AnglesFor("queue");
        Assert.Equal(a, ScrewLayout.AnglesFor("queue"));
        Assert.NotEqual(a, ScrewLayout.AnglesFor("source"));
        Assert.Equal(4, a.Count);
        Assert.All(a, d => Assert.InRange(d, 0, 59));
    }

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void Both_modes_supply_every_token(LookMode m)
    {
        var t = LookTokens.For(m);
        Assert.All(LookTokens.RequiredKeys, k => Assert.False(string.IsNullOrEmpty(t[k])));
    }

    [Fact]
    public void Dark_plate_is_darker_than_light_plate()
    {
        Assert.Equal("#3A3D41", LookTokens.For(LookMode.Dark)["PlateTop"]);
        Assert.Equal("#E0E2E5", LookTokens.For(LookMode.Light)["PlateTop"]);
    }

    [Fact]
    public void Ink_meets_contrast_on_both_plates()
    {
        // WCAG AA is 4.5:1 for text. Measured against the plate's darkest or lightest stop, whichever is worse.
        foreach (var m in new[] { LookMode.Dark, LookMode.Light })
        {
            var t = LookTokens.For(m);
            foreach (var plate in new[] { "PlateTop", "PlateMid", "PlateBottom" })
                Assert.True(LookTokens.Contrast(t["Ink"], t[plate]) >= 4.5, $"{m} Ink on {plate}");
        }
    }

    // The user guide sets pad names in Accent and each section's summary in InkDim, on the same plates as Ink.
    [Fact]
    public void Accent_and_dim_ink_meet_contrast_on_both_plates()
    {
        foreach (var m in new[] { LookMode.Dark, LookMode.Light })
        {
            var t = LookTokens.For(m);
            foreach (var ink in new[] { "Accent", "InkDim" })
                foreach (var plate in new[] { "PlateTop", "PlateMid", "PlateBottom" })
                    Assert.True(LookTokens.Contrast(t[ink], t[plate]) >= 4.5, $"{m} {ink} on {plate}");
        }
    }
}
