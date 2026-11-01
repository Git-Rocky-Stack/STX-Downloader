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

    const int Bezel = 144, Cap = 114;

    [Fact]
    public void Switch_bezel_catches_light_on_its_top_edge_and_falls_dark_below()
    {
        var bezel = ControlArt.SwitchBezel(Bezel);
        Assert.True(bezel.Luma(72, 4) > bezel.Luma(72, 139) + 40);
    }

    [Fact]
    public void Switch_bezel_has_rounded_corners_and_a_dark_opening()
    {
        var bezel = ControlArt.SwitchBezel(Bezel);
        Assert.Equal(0, bezel.Alpha(0, 0));
        Assert.Equal(255, bezel.Alpha(72, 72));
        Assert.True(bezel.Luma(72, 72) < 30);
    }

    [Fact]
    public void Switch_cap_is_lit_from_above()
    {
        var cap = ControlArt.SwitchCap(Cap);
        Assert.True(cap.Luma(57, 50) > cap.Luma(57, 100) + 10);
    }

    [Fact]
    public void Switch_cap_has_fine_brushed_grain()
    {
        var cap = ControlArt.SwitchCap(Cap);
        // Open metal only: the slit and the cap's edges are not grain. The gradient itself adds under 0.5 a row.
        double sum = 0; int n = 0;
        for (int y = 51; y < 100; y++) for (int x = 20; x < 94; x++) { sum += Math.Abs(cap.Luma(x, y) - cap.Luma(x, y - 1)); n++; }
        Assert.InRange(sum / n, 0.8, 3);
    }

    [Fact]
    public void Switch_cap_has_a_dark_slit_for_its_led()
    {
        var cap = ControlArt.SwitchCap(Cap);
        Assert.True(cap.Luma(57, 24) < 25);
        Assert.True(cap.Luma(10, 24) > cap.Luma(57, 24) + 25, "the slit does not run edge to edge");
    }

    [Fact]
    public void Switch_led_fills_the_slit_and_glows_onto_the_cap()
    {
        var led = ControlArt.SwitchLed(Cap, LedColor.Green);
        Assert.True(led.Luma(57, 24) > 150);
        Assert.True(led.Alpha(57, 38) > 20, "glow below the slit");
        Assert.Equal(0, led.Alpha(57, 100));
    }

    [Fact]
    public void Switch_led_glow_fades_before_the_cap_edge()
    {
        var led = ControlArt.SwitchLed(Cap, LedColor.Green);
        Assert.True(led.Alpha(57, 0) <= 2);
        Assert.True(led.Alpha(0, 24) <= 2);
    }

    [Theory]
    [InlineData(LedColor.Green)]
    [InlineData(LedColor.Amber)]
    public void Switch_led_takes_its_color(LedColor color)
    {
        var led = ControlArt.SwitchLed(Cap, color);
        var glow = Rgb(led, 57, 34);
        Assert.True(color == LedColor.Green ? glow.G > glow.R : glow.R > glow.B);
    }

    [Fact]
    public void Switch_press_shade_darkens_the_top_of_the_cap_only()
    {
        var shade = ControlArt.SwitchCapShade(Cap);
        Assert.True(shade.Alpha(57, 3) > 100);
        Assert.True(shade.Alpha(57, 90) < 10);
    }

    [Fact]
    public void Control_art_is_deterministic_so_baselines_hold() =>
        Assert.Equal(ControlArt.Knurl(Knob).Hash(), ControlArt.Knurl(Knob).Hash());
}

public class SpectrumArtTests
{
    static readonly double[] Hz = Enumerable.Range(0, 48).Select(i => 20 * Math.Pow(1200, i / 47.0)).ToArray();
    static readonly double[] Db = Enumerable.Range(0, 48).Select(i => i < 40 ? -20.0 - i * 0.5 : -120.0).ToArray();

    [Fact]
    public void No_data_draws_nothing()
    {
        var art = SpectrumArt.Render(480, 120, [], [], null, DisplayTone.Cyan);
        Assert.Equal(0, art.MeanLuma());
    }

    [Fact]
    public void Louder_bands_stand_taller()
    {
        var art = SpectrumArt.Render(480, 120, Hz, Db, null, DisplayTone.Cyan);
        int Height(int x) => Enumerable.Range(0, 120).Count(y => art.Alpha(x, y) > 40);
        Assert.True(Height(20) > Height(300));
        Assert.Equal(0, Height(470));
    }

    [Fact]
    public void The_roll_off_is_marked_where_it_was_measured()
    {
        var art = SpectrumArt.Render(480, 120, Hz, Db, 2000, DisplayTone.Cyan);
        int x = SpectrumArt.XFor(2000, Hz, 480);
        Assert.True(art.Alpha(x, 2) > 200, "marker runs the full height");
        Assert.True(art.Alpha(x, 117) > 200);
    }

    [Fact]
    public void Without_a_roll_off_there_is_no_marker()
    {
        var art = SpectrumArt.Render(480, 120, Hz, Db, null, DisplayTone.Cyan);
        Assert.Equal(0, art.Alpha(SpectrumArt.XFor(2000, Hz, 480), 2));
    }

    [Fact]
    public void A_full_band_file_gets_no_marker_at_the_edge()
    {
        var art = SpectrumArt.Render(480, 120, Hz, Db, Hz[^1] * 2, DisplayTone.Cyan);
        Assert.Equal(0, art.Alpha(479, 2));
    }

    [Fact]
    public void Same_data_draws_the_same_picture() =>
        Assert.Equal(SpectrumArt.Render(240, 60, Hz, Db, 2000, DisplayTone.Amber).Hash(), SpectrumArt.Render(240, 60, Hz, Db, 2000, DisplayTone.Amber).Hash());
}
