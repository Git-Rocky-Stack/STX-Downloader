using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace Stx.VisualTests;

public sealed class ScreenshotDiffTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-diff").FullName;

    string Png(string name, Action<Bitmap> paint)
    {
        var path = Path.Combine(_dir, name + ".png");
        using var b = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 50, 60, 70));
        paint(b);
        b.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Identical_images_pass()
    {
        var r = ScreenshotDiff.Compare(Png("a", _ => { }), Png("b", _ => { }));
        Assert.True(r.Pass);
        Assert.Equal(0, r.ChangedRatio);
    }

    [Fact]
    public void One_percent_of_pixels_changed_by_40_fails()
    {
        var changed = Png("c", b =>
        {
            for (int x = 0; x < 100; x++) b.SetPixel(x, 0, Color.FromArgb(255, 90, 100, 110));
        });
        var r = ScreenshotDiff.Compare(changed, Png("d", _ => { }));
        Assert.False(r.Pass);
        Assert.Equal(0.01, r.ChangedRatio, 5);
    }

    [Fact]
    public void Channel_differences_of_4_or_less_are_ignored()
    {
        var near = Png("e", b =>
        {
            for (int x = 0; x < 100; x++) for (int y = 0; y < 100; y++) b.SetPixel(x, y, Color.FromArgb(255, 54, 56, 74));
        });
        var r = ScreenshotDiff.Compare(near, Png("f", _ => { }));
        Assert.True(r.Pass);
        Assert.Equal(0, r.ChangedRatio);
    }

    [Fact]
    public void Half_percent_changed_still_passes()
    {
        var changed = Png("g", b =>
        {
            for (int x = 0; x < 50; x++) b.SetPixel(x, 0, Color.FromArgb(255, 90, 100, 110));
        });
        Assert.True(ScreenshotDiff.Compare(changed, Png("h", _ => { })).Pass);
    }

    [Fact]
    public void Different_sizes_fail()
    {
        var other = Path.Combine(_dir, "i.png");
        using (var b = new Bitmap(50, 50)) b.Save(other, ImageFormat.Png);
        var r = ScreenshotDiff.Compare(other, Png("j", _ => { }));
        Assert.False(r.Pass);
        Assert.Equal(1, r.ChangedRatio);
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
