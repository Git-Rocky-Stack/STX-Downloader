using System.Numerics;
using System.Xml.Linq;
using Stx.Look;
using Xunit;

namespace Stx.Look.Tests;

public class ControlLogicTests
{
    [Fact]
    public void Eight_lights_all_seven_segments() =>
        Assert.Equal(7, BitOperations.PopCount((uint)SevenSegment.For('8')));

    [Fact]
    public void Space_lights_none() =>
        Assert.Equal(Segments.None, SevenSegment.For(' '));

    [Fact]
    public void A_character_with_no_shape_lights_none() =>
        Assert.Equal(Segments.None, SevenSegment.For('x'));

    [Theory]
    [InlineData('0', "abcdef")]
    [InlineData('1', "bc")]
    [InlineData('2', "abged")]
    [InlineData('3', "abgcd")]
    [InlineData('4', "fgbc")]
    [InlineData('5', "afgcd")]
    [InlineData('6', "afgedc")]
    [InlineData('7', "abc")]
    [InlineData('9', "abcdfg")]
    public void Digits_light_the_right_segments(char digit, string lit)
    {
        Segments expected = Segments.None;
        foreach (char s in lit) expected |= (Segments)(1 << (s - 'a'));
        Assert.Equal(expected, SevenSegment.For(digit));
    }

    [Theory]
    [InlineData(0, 7, -135)]
    [InlineData(3, 7, 0)]
    [InlineData(6, 7, 135)]
    [InlineData(1, 2, 135)]
    [InlineData(0, 1, 0)]
    [InlineData(9, 7, 135)]
    [InlineData(-2, 7, -135)]
    public void Detent_angles_spread_across_the_270_degree_sweep(int index, int count, double angle) =>
        Assert.Equal(angle, Detents.Angle(index, count));

    [Theory]
    [InlineData(6, 7, 0)]
    [InlineData(0, 7, 1)]
    [InlineData(0, 0, 0)]
    public void Next_detent_wraps(int index, int count, int next) =>
        Assert.Equal(next, Detents.Next(index, count));

    [Theory]
    [InlineData(0, 7, 6)]
    [InlineData(3, 7, 2)]
    [InlineData(0, 0, 0)]
    public void Previous_detent_wraps(int index, int count, int previous) =>
        Assert.Equal(previous, Detents.Previous(index, count));

    [Fact]
    public void Scale_point_for_angle_zero_is_straight_up()
    {
        var (x, y) = Detents.Point(0, 72);
        Assert.Equal(0, x, 6);
        Assert.Equal(-72, y, 6);
    }

    [Fact]
    public void Scale_starts_lower_left()
    {
        var (x, y) = Detents.Point(Detents.Angle(0, 7), 72);
        Assert.True(x < 0 && y > 0);
    }

    [Fact]
    public void Press_travel_is_three_pixels() => Assert.Equal(3, PressTravel.Pixels);

    [Fact]
    public void Turning_overshoots_normally() => Assert.True(PressTravel.TurnEasing(reduceMotion: false).Y1 > 1);

    [Fact]
    public void Reduce_motion_skips_overshoot()
    {
        var easing = PressTravel.TurnEasing(reduceMotion: true);
        Assert.True(easing.Y1 <= 1 && easing.Y2 <= 1);
    }

    [Theory]
    [InlineData(PadTone.Off)]
    [InlineData(PadTone.Green)]
    [InlineData(PadTone.Blue)]
    public void Pad_legends_meet_aa_on_their_face(PadTone tone) =>
        Assert.True(LookTokens.Contrast(PadTones.Legend(tone), PadTones.Center(tone)) >= 4.5);

    static XElement PadStyle(string target)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        var xaml = XDocument.Load(Path.Combine(dir!.FullName, "src", "Stx.App", "Themes", "Generic.xaml"));
        return xaml.Root!.Elements().First(e => e.Name.LocalName == "Style" && (string?)e.Attribute("TargetType") == target);
    }

    static double Setter(XElement style, string property) =>
        double.Parse(style.Elements().First(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == property).Attribute("Value")!.Value);

    [Fact]
    public void Pad_template_travels_exactly_the_press_travel()
    {
        var pressed = PadStyle("p:Pad").Descendants().First(e => e.Name.LocalName == "VisualState" && e.Attributes().Any(a => a.Value == "Pressed"));
        var move = pressed.Descendants().First(e => e.Name.LocalName == "DoubleAnimation" && e.Attributes().Any(a => a.Value == "PressOffset"));
        Assert.Equal(PressTravel.Pixels, double.Parse(move.Attribute("To")!.Value));
    }

    [Fact]
    public void Pad_hover_is_a_subtle_lift()
    {
        var hover = PadStyle("p:Pad").Descendants().First(e => e.Name.LocalName == "VisualState" && e.Attributes().Any(a => a.Value == "PointerOver"));
        var glow = hover.Descendants().First(e => e.Name.LocalName == "DoubleAnimation" && e.Attributes().Any(a => a.Value == "HoverGlow"));
        Assert.InRange(double.Parse(glow.Attribute("To")!.Value), 0.02, 0.06);
    }

    [Theory]
    [InlineData("p:Faceplate", "PART_Plate")]
    [InlineData("p:Display", "PART_Screen")]
    [InlineData("p:Display", "PART_Glass")]
    public void Panel_art_sits_on_a_canvas_so_its_pixels_cannot_size_the_panel(string target, string part)
    {
        var art = PadStyle(target).Descendants().First(e => e.Attributes().Any(a => a.Value == part));
        Assert.Equal("Canvas", art.Parent!.Name.LocalName);
    }

    [Fact]
    public void A_latched_switch_stays_part_way_in() =>
        Assert.InRange(PressTravel.LatchedPixels, 1, PressTravel.Pixels - 1);

    static double StateTarget(string state, string target)
    {
        var visual = PadStyle("p:PushSwitch").Descendants().First(e => e.Name.LocalName == "VisualState" && e.Attributes().Any(a => a.Value == state));
        var move = visual.Descendants().First(e => e.Name.LocalName == "DoubleAnimation" && e.Attributes().Any(a => a.Value == target));
        return double.Parse(move.Attribute("To")!.Value);
    }

    [Theory]
    [InlineData("Normal", 0)]
    [InlineData("PointerOver", 0)]
    [InlineData("Pressed", PressTravel.Pixels)]
    [InlineData("Disabled", 0)]
    [InlineData("Checked", PressTravel.LatchedPixels)]
    [InlineData("CheckedPointerOver", PressTravel.LatchedPixels)]
    [InlineData("CheckedPressed", PressTravel.Pixels)]
    [InlineData("CheckedDisabled", PressTravel.LatchedPixels)]
    public void Switch_cap_sits_where_its_state_says(string state, double pixels) =>
        Assert.Equal(pixels, StateTarget(state, "CapOffset"));

    [Theory]
    [InlineData("Normal", 0)]
    [InlineData("PointerOver", 0)]
    [InlineData("Pressed", 0)]
    [InlineData("Disabled", 0)]
    [InlineData("Checked", 1)]
    [InlineData("CheckedPointerOver", 1)]
    [InlineData("CheckedPressed", 1)]
    [InlineData("CheckedDisabled", 1)]
    public void Switch_led_is_lit_only_when_latched(string state, double opacity) =>
        Assert.Equal(opacity, StateTarget(state, "PART_Led"));

    [Theory]
    [InlineData("Normal")]
    [InlineData("PointerOver")]
    [InlineData("Pressed")]
    [InlineData("Disabled")]
    public void Pad_template_has_every_button_state(string state) =>
        Assert.Contains(PadStyle("p:Pad").Descendants(), e => e.Name.LocalName == "VisualState" && e.Attributes().Any(a => a.Value == state));

    [Theory]
    [InlineData("p:Pad")]
    [InlineData("p:PushSwitch")]
    public void Hit_targets_are_at_least_44_px(string target)
    {
        var style = PadStyle(target);
        Assert.True(Setter(style, "MinHeight") >= 44);
        Assert.True(Setter(style, "MinWidth") >= 44);
    }

    [Theory]
    [InlineData("p:Pad")]
    [InlineData("p:PushSwitch")]
    [InlineData("p:Rotary")]
    public void Controls_show_keyboard_focus(string target) =>
        Assert.Contains(PadStyle(target).Elements(), e =>
            (string?)e.Attribute("Property") == "UseSystemFocusVisuals" && (string?)e.Attribute("Value") == "True");
}
