using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.Art.Tests;

public class PlateAndScrewArtTests
{
    [Theory]
    [InlineData(400, 200, LookMode.Dark, "C8E6F6D8E141B7315E2C8D4CB8C922C9B7163FFDE7EE4864A1B26640EADC022E")]
    [InlineData(400, 200, LookMode.Light, "24EFC2A88F34C879EF54C5BCCE03A036EE9997427E66BCF5675A8E8081E175CF")]
    [InlineData(64, 64, LookMode.Dark, "B8B448903B11CA2AB9A57C63E67795B8F904B6DC162E78AB0198732B1ABF3418")]
    [InlineData(1, 1, LookMode.Light, "AD95131BC0B799C0B1AF477FB14FCF26A6A9F76079E48BF090ACB7E8367BFD0E")]
    public void Render_is_unchanged(int w, int h, LookMode m, string hash) =>
        Assert.Equal(hash, PlateArt.Render(w, h, m).Hash());

    [Fact]
    public void GrainTile_wraps_horizontally()
    {
        var t = PlateArt.GrainTile(256, 64, LookMode.Dark, 1);
        double seam = 0;
        for (int y = 0; y < 64; y++) seam += Math.Abs(t.Luma(0, y) - t.Luma(255, y));
        seam /= 64;
        Assert.True(seam < t.MeanAbsDeltaHorizontal() * 1.5, $"seam {seam:F3} vs interior {t.MeanAbsDeltaHorizontal():F3}");
    }

    [Theory]
    [InlineData(LookMode.Dark)]
    [InlineData(LookMode.Light)]
    public void GrainTile_is_neutral(LookMode m) =>
        Assert.InRange(PlateArt.GrainTile(256, 64, m, 1).MeanLuma(), 127, 129);

    [Fact]
    public void GrainTile_grain_runs_horizontally()
    {
        var t = PlateArt.GrainTile(256, 64, LookMode.Dark, 1);
        Assert.True(t.MeanAbsDeltaVertical() > t.MeanAbsDeltaHorizontal() * 2);
    }

    [Fact]
    public void GrainTile_gain_scales_texture()
    {
        double one = PlateArt.GrainTile(256, 64, LookMode.Dark, 1).MeanAbsDeltaVertical();
        double three = PlateArt.GrainTile(256, 64, LookMode.Dark, 3).MeanAbsDeltaVertical();
        Assert.InRange(three / one, 2.5, 3.5);
    }

    [Fact]
    public void GrainTile_is_deterministic() =>
        Assert.Equal(PlateArt.GrainTile(64, 32, LookMode.Light, 2).Hash(), PlateArt.GrainTile(64, 32, LookMode.Light, 2).Hash());

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
