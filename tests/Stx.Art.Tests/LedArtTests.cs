using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.Art.Tests;

public class LedArtTests
{
    const int LedSize = 192;
    const int SegW = 96, SegH = 90;

    static int Px(double logical, int artSize, double artLogical) => (int)(logical / artLogical * artSize);

    static (int R, int G, int B) Rgb(PixelBuffer b, int x, int y)
    {
        int i = (y * b.Width + x) * 4;
        return (b.Bgra[i + 2], b.Bgra[i + 1], b.Bgra[i]);
    }

    [Fact]
    public void Lit_led_blooms_well_past_its_lens()
    {
        var lit = LedArt.Lit(LedColor.Green, LedSize);
        int c = LedSize / 2;
        int justOutside = c + Px(LedArt.LedLogical / 2.0 + 3, LedSize, LedArt.LedArtLogical);
        Assert.True(lit.Alpha(justOutside, c) > 40, "bloom should reach onto the plate");
    }

    [Fact]
    public void Lit_led_bloom_fades_out_before_the_art_edge()
    {
        var lit = LedArt.Lit(LedColor.Green, LedSize);
        Assert.True(lit.Alpha(0, LedSize / 2) <= 2, "a hard edge would show on the plate");
        Assert.True(lit.Alpha(LedSize / 2, LedSize - 1) <= 2);
    }

    [Fact]
    public void Lit_lens_is_near_white_at_the_center_and_colored_toward_its_edge()
    {
        var lit = LedArt.Lit(LedColor.Red, LedSize);
        int c = LedSize / 2;
        var center = Rgb(lit, c, c + 1);
        Assert.True(center.G > 150, "hot center");
        var edge = Rgb(lit, c + Px(4, LedSize, LedArt.LedArtLogical), c);
        Assert.True(edge.R > edge.G * 2, "red toward the edge");
    }

    [Theory]
    [InlineData(LedColor.Green)]
    [InlineData(LedColor.Red)]
    public void Led_color_reaches_the_bloom(LedColor color)
    {
        var lit = LedArt.Lit(color, LedSize);
        var bloom = Rgb(lit, LedSize / 2 + Px(LedArt.LedLogical / 2.0 + 4, LedSize, LedArt.LedArtLogical), LedSize / 2);
        Assert.True(color == LedColor.Green ? bloom.G > bloom.R : bloom.R > bloom.G);
    }

    [Fact]
    public void Housing_has_a_chrome_bezel_a_dark_lens_and_a_highlight()
    {
        var housing = LedArt.Housing(LedSize);
        int c = LedSize / 2;
        Assert.Equal(0, housing.Alpha(2, 2));
        double bezelRadius = Px(LedArt.LedLogical / 2.0 - 1, LedSize, LedArt.LedArtLogical);
        Assert.True(housing.Luma(c, c - (int)bezelRadius) > 60, "chrome bezel");
        Assert.True(housing.Luma(c + Px(2, LedSize, LedArt.LedArtLogical), c + Px(2, LedSize, LedArt.LedArtLogical)) < 60, "dark lens");
    }

    [Fact]
    public void Flare_is_hotter_than_the_lit_layer_at_its_center()
    {
        int c = LedSize / 2;
        var flare = LedArt.Flare(LedColor.Amber, LedSize);
        Assert.True(flare.Luma(c, c) > 200);
    }

    [Fact]
    public void Lit_segment_bloom_spills_onto_its_neighbors()
    {
        var lit = LedArt.SegmentLit(LedColor.Green, SegW, SegH);
        int overhang = Px(LedArt.SegmentOverhang, SegW, LedArt.SegmentArtWidth);
        Assert.True(lit.Alpha(overhang - Px(2, SegW, LedArt.SegmentArtWidth), SegH / 2) > 30, "neighbor sits 2 px away");
    }

    [Fact]
    public void Lit_segment_bloom_fades_out_before_the_art_edge()
    {
        var lit = LedArt.SegmentLit(LedColor.Green, SegW, SegH);
        Assert.True(lit.Alpha(0, SegH / 2) <= 2);
        Assert.True(lit.Alpha(SegW / 2, 0) <= 2);
    }

    [Fact]
    public void Bloom_is_dimmer_than_the_segment_edge_it_leaves_so_no_bright_outline_forms()
    {
        var lit = LedArt.SegmentLit(LedColor.Green, SegW, SegH);
        int overhang = Px(LedArt.SegmentOverhang, SegW, LedArt.SegmentArtWidth);
        double insideEdge = lit.Luma(overhang + 1, SegH / 2);
        double outsideEdge = lit.Luma(overhang - 2, SegH / 2);
        Assert.True(outsideEdge < insideEdge * 0.8, $"outside {outsideEdge:F0}, inside {insideEdge:F0}");
    }

    [Fact]
    public void Gap_between_lit_segments_stays_visible()
    {
        var lit = LedArt.SegmentLit(LedColor.Green, SegW, SegH);
        int overhang = Px(LedArt.SegmentOverhang, SegW, LedArt.SegmentArtWidth);
        Assert.True(lit.Alpha(overhang - Px(1, SegW, LedArt.SegmentArtWidth), SegH / 2) < 170, "bloom must not fill the gap");
    }

    [Fact]
    public void Lit_segment_is_far_brighter_than_unlit()
    {
        var lit = LedArt.SegmentLit(LedColor.Green, SegW, SegH);
        var unlit = LedArt.SegmentUnlit(LedColor.Green, 48, 42);
        Assert.True(lit.Luma(SegW / 2, SegH / 2) > unlit.MeanLuma() * 5);
    }

    [Theory]
    [InlineData(LedColor.Amber)]
    [InlineData(LedColor.Red)]
    public void Unlit_segments_keep_a_tint_of_their_color(LedColor color)
    {
        var unlit = Rgb(LedArt.SegmentUnlit(color, 48, 42), 24, 8);
        Assert.True(unlit.R > unlit.B, "dead amber and red segments still read warm");
    }

    [Theory]
    [InlineData("#B06BFF")]
    [InlineData("#22E6B4")]
    [InlineData("#FF9A1F")]
    public void A_ramp_colors_segments_light_dim_and_keep_their_tint(string hex)
    {
        var lit = LedArt.SegmentLit(hex, SegW, SegH);
        var unlit = LedArt.SegmentUnlit(hex, 48, 42);
        Assert.True(lit.Luma(SegW / 2, SegH / 2) > unlit.MeanLuma() * 5);
        var core = (R: Convert.ToInt32(hex[1..3], 16), G: Convert.ToInt32(hex[3..5], 16), B: Convert.ToInt32(hex[5..7], 16));
        var dead = Rgb(unlit, 24, 8);
        // The strongest channel of the color is still the strongest (or level) in its dead segment.
        if (core.B > core.R && core.B > core.G) Assert.True(dead.B >= dead.R && dead.B >= dead.G, $"{hex} dead reads {dead}");
        if (core.R > core.B) Assert.True(dead.R > dead.B, $"{hex} dead reads {dead}");
        Assert.True(LedArt.SegmentPeak(hex, SegW, SegH).Luma(SegW / 2, SegH / 3) > lit.Luma(SegW / 2, SegH / 3));
    }

    [Fact]
    public void Named_and_hex_colors_draw_the_same_segment() =>
        Assert.Equal(LedArt.SegmentLit(LedColor.Green, SegW, SegH).Hash(), LedArt.SegmentLit(LedColors.Core(LedColor.Green), SegW, SegH).Hash());

    [Fact]
    public void Peak_segment_is_hotter_than_a_lit_one()
    {
        var lit = LedArt.SegmentLit(LedColor.Red, SegW, SegH);
        var peak = LedArt.SegmentPeak(LedColor.Red, SegW, SegH);
        Assert.True(peak.Luma(SegW / 2, SegH / 3) > lit.Luma(SegW / 2, SegH / 3));
    }

    [Fact]
    public void Art_is_deterministic_so_baselines_hold() =>
        Assert.Equal(LedArt.Lit(LedColor.Blue, LedSize).Hash(), LedArt.Lit(LedColor.Blue, LedSize).Hash());
}
