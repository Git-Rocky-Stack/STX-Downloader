using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.Art.Tests;

public class PlateAndScrewArtTests
{
    [Fact]
    public void Plate_grain_runs_horizontally()
    {
        var p = PlateArt.Render(400, 200, LookMode.Dark);
        Assert.True(p.MeanAbsDeltaVertical() > p.MeanAbsDeltaHorizontal() * 2);
    }

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void Plate_has_visible_grain_not_a_flat_color(LookMode m) =>
        Assert.True(PlateArt.Render(400, 200, m).MeanAbsDeltaVertical() > 1);

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void Grain_stays_faint(LookMode m)
    {
        // The brief asks for a faint texture, not a striped one.
        double delta = PlateArt.Render(400, 200, m).MeanAbsDeltaVertical();
        Assert.True(delta < 3, $"vertical delta was {delta:F2}");
    }

    [Fact]
    public void Art_is_deterministic() =>
        Assert.Equal(PlateArt.Render(64, 64, LookMode.Light).Hash(), PlateArt.Render(64, 64, LookMode.Light).Hash());

    [Fact]
    public void Dark_and_light_plates_differ_in_brightness() =>
        Assert.True(PlateArt.Render(64, 64, LookMode.Light).MeanLuma() > PlateArt.Render(64, 64, LookMode.Dark).MeanLuma() + 80);

    [Fact]
    public void Plate_is_fully_opaque() =>
        Assert.Equal(255, PlateArt.Render(32, 32, LookMode.Dark).Alpha(16, 16));

    [Fact]
    public void Screw_has_transparent_corners_and_a_dark_hex_socket()
    {
        var s = ScrewArt.Render(72, 0);
        Assert.Equal(0, s.Alpha(0, 0));
        Assert.Equal(255, s.Alpha(36, 36));
        Assert.True(s.Luma(36, 36) < 90, "socket should be dark");
        Assert.True(s.Luma(18, 36) > s.Luma(36, 36), "steel should be brighter than the socket");
    }

    [Fact]
    public void Screw_rotation_changes_the_socket()
    {
        Assert.NotEqual(ScrewArt.Render(72, 0).Hash(), ScrewArt.Render(72, 30).Hash());
    }
}
