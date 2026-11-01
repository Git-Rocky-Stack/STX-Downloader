using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.Art.Tests;

public class ControlArtTests
{
    const int DigitW = 75, DigitH = 105;
    const int Knob = 300;

    static (int R, int G, int B) Rgb(PixelBuffer b, int x, int y)
    {
        int i = (y * b.Width + x) * 4;
        return (b.Bgra[i + 2], b.Bgra[i + 1], b.Bgra[i]);
    }

    /// <summary>Middle of the top bar of a digit, in art pixels.</summary>
    static (int X, int Y) TopBar() => ((int)((ControlArt.DigitGlow + 6.5) * 3), (int)((ControlArt.DigitGlow + 1) * 3));

    [Fact]
    public void Digit_lights_only_its_own_segments()
    {
        var (x, y) = TopBar();
        Assert.True(ControlArt.Digit('8', DigitW, DigitH).Luma(x, y) > 120, "8 has a top bar");
        Assert.True(ControlArt.Digit('1', DigitW, DigitH).Luma(x, y) < 40, "1 has none, only the dead segment");
    }

    [Fact]
    public void Dead_segments_stay_faintly_visible()
    {
        var (x, y) = TopBar();
        Assert.True(ControlArt.Digit(' ', DigitW, DigitH).Alpha(x, y) > 200, "real displays show their unlit bars");
    }

    [Fact]
    public void Lit_digit_glows_past_its_segments()
    {
        var digit = ControlArt.Digit('8', DigitW, DigitH);
        var blank = ControlArt.Digit(' ', DigitW, DigitH);
        int x = DigitW / 2, y = (int)((ControlArt.DigitGlow - 1) * 3);
        Assert.True(digit.Alpha(x, y) > 20);
        Assert.Equal(0, blank.Alpha(x, y));
    }

    [Fact]
    public void Digit_glow_fades_out_before_the_art_edge()
    {
        var digit = ControlArt.Digit('8', DigitW, DigitH);
        Assert.True(digit.Alpha(0, DigitH / 2) <= 3);
        Assert.True(digit.Alpha(DigitW / 2, 0) <= 3);
    }

    [Fact]
    public void Knurl_has_ridges()
    {
        var knurl = ControlArt.Knurl(Knob);
        var seen = new HashSet<int>();
        double r = Knob * 0.46;
        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 60;
            seen.Add((int)knurl.Luma((int)(Knob / 2 + r * Math.Cos(a)), (int)(Knob / 2 + r * Math.Sin(a))) / 16);
        }
        Assert.True(seen.Count >= 2, "ridges alternate light and dark");
    }

    [Fact]
    public void Knurl_carries_no_directional_light_so_highlights_stay_fixed_when_it_turns()
    {
        var knurl = ControlArt.Knurl(Knob);
        double Quadrant(int fromX, int fromY)
        {
            double sum = 0;
            for (int y = fromY; y < fromY + Knob / 2; y++) for (int x = fromX; x < fromX + Knob / 2; x++) sum += knurl.Luma(x, y);
            return sum;
        }
        double tl = Quadrant(0, 0), br = Quadrant(Knob / 2, Knob / 2);
        Assert.InRange(tl / br, 0.95, 1.05);
    }

    [Fact]
    public void Knob_shade_lights_the_top_left_and_darkens_the_bottom_right()
    {
        var shade = ControlArt.KnobShade(Knob);
        int near = Knob / 4, far = Knob * 3 / 4;
        Assert.True(shade.Luma(near, near) > 15, "white highlight top left");
        Assert.True(shade.Alpha(far, far) > 50 && shade.Luma(far, far) < 8, "black shade bottom right");
    }

    [Fact]
    public void Cap_is_spun_metal_with_fixed_bright_and_dark_bands()
    {
        var cap = ControlArt.KnobCap(Knob);
        var lumas = new List<double>();
        double r = Knob * 0.25;
        for (int i = 0; i < 72; i++)
        {
            double a = i * Math.PI / 36;
            lumas.Add(cap.Luma((int)(Knob / 2 + r * Math.Cos(a)), (int)(Knob / 2 + r * Math.Sin(a))));
        }
        Assert.True(lumas.Max() - lumas.Min() > 90);
    }

    [Fact]
    public void Cap_leaves_the_knurl_showing_around_it() =>
        Assert.Equal(0, ControlArt.KnobCap(Knob).Alpha(Knob / 2, 3));

    [Fact]
    public void Indicator_points_straight_up_before_it_is_rotated()
    {
        var indicator = ControlArt.Indicator(Knob);
        Assert.True(indicator.Alpha(Knob / 2, Knob / 6) > 200, "bar near the top");
        Assert.True(indicator.Luma(Knob / 2, Knob / 6) > 180, "and it is bright");
        Assert.Equal(0, indicator.Alpha(Knob / 2, Knob * 3 / 4));
    }

    [Theory]
    [InlineData(PadTone.Off)]
    [InlineData(PadTone.Green)]
    [InlineData(PadTone.Blue)]
    public void Pad_face_center_matches_the_color_its_legend_was_checked_against(PadTone tone)
    {
        var face = ControlArt.PadFace(184, 78, 2, tone);
        var (r, g, b) = Rgb(face, 92, 39);
        string hex = PadTones.Center(tone);
        int Channel(int i) => Convert.ToInt32(hex.Substring(i, 2), 16);
        Assert.InRange(r, Channel(1) - 40, Channel(1) + 40);
        Assert.InRange(g, Channel(3) - 40, Channel(3) + 40);
        Assert.InRange(b, Channel(5) - 40, Channel(5) + 40);
    }

    [Fact]
    public void Pad_face_is_never_flat_top_catches_light_and_bottom_falls_away()
    {
        var face = ControlArt.PadFace(184, 78, 2, PadTone.Off);
        Assert.True(face.Luma(92, 6) > face.Luma(92, 72) + 25);
    }

    [Fact]
    public void Pad_gloss_melts_into_the_rubber_with_no_hard_edges()
    {
        var face = ControlArt.PadFace(184, 78, 2, PadTone.Off);
        double worst = 0;
        for (int y = 8; y < 26; y++)
            for (int x = 4; x < 180; x++)
                worst = Math.Max(worst, Math.Abs(face.Luma(x, y) - face.Luma(x - 1, y)));
        Assert.True(worst < 3, $"step of {worst:F1} between neighboring pixels");
        double down = 0;
        for (int y = 8; y < 40; y++) down = Math.Max(down, Math.Abs(face.Luma(92, y) - face.Luma(92, y - 1)));
        Assert.True(down < 3, $"step of {down:F1} going down the pad");
    }

    [Fact]
    public void Pad_face_corners_are_rounded() =>
        Assert.Equal(0, ControlArt.PadFace(184, 78, 2, PadTone.Off).Alpha(0, 0));

    [Fact]
    public void Press_shade_darkens_the_top_inner_edge_only()
    {
        var shade = ControlArt.PadPressShade(184, 78, 2);
        Assert.True(shade.Alpha(92, 3) > 100);
        Assert.True(shade.Alpha(92, 60) < 10);
    }

    [Fact]
    public void Lit_pad_bloom_surrounds_the_pad_and_fades_out()
    {
        var bloom = ControlArt.PadBloom(280, 200, 2, PadTone.Green);
        Assert.Equal(0, bloom.Alpha(140, 100));
        Assert.True(bloom.Alpha(140, 2 * 24) > 40, "glow lands just above the pad");
        Assert.True(bloom.Alpha(140, 0) <= 2);
    }

    [Fact]
    public void Well_edge_catches_more_light_on_brushed_aluminum()
    {
        var dark = ControlArt.PadWell(200, 112, 2, LookMode.Dark);
        var light = ControlArt.PadWell(200, 112, 2, LookMode.Light);
        Assert.True(light.Luma(100, 111) > dark.Luma(100, 111));
    }

    [Fact]
    public void Nut_is_a_hexagon()
    {
        var nut = ControlArt.Nut(120);
        Assert.Equal(0, nut.Alpha(4, 4));
        Assert.True(nut.Alpha(60, 16) > 200, "flat top");
        Assert.True(nut.Alpha(8, 60) > 200, "side vertex");
    }

    [Fact]
    public void Collar_is_threaded()
    {
        var collar = ControlArt.Collar(78);
        var seen = new HashSet<int>();
        for (int x = 39; x < 72; x++) seen.Add((int)collar.Luma(x, 39) / 32);
        Assert.True(seen.Count >= 2);
    }

    [Fact]
    public void Lever_hangs_from_its_hinge_with_a_ball_tip()
    {
        var lever = ControlArt.Lever(48, 96);
        Assert.True(lever.Alpha(24, 2) > 200, "starts at the hinge");
        Assert.Equal(0, lever.Alpha(6, 10));
        Assert.True(lever.Alpha(6, 78) > 200, "ball is wider than the shaft");
    }

    [Fact]
    public void Control_art_is_deterministic_so_baselines_hold() =>
        Assert.Equal(ControlArt.Knurl(Knob).Hash(), ControlArt.Knurl(Knob).Hash());
}
