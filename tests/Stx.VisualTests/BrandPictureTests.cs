using System.Drawing;
using Xunit;

namespace Stx.VisualTests;

/// <summary>The badge and icon are the pictures Rocky chose, with only their black background taken away.</summary>
public class BrandPictureTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Assets(string name) => Path.Combine(Root(), "src", "Stx.App", "Assets", name);

    [Fact]
    public void The_badge_is_the_chosen_picture_without_its_background()
    {
        using var chosen = new Bitmap(Path.Combine(Root(), "icons", "badge_1.png"));
        using var shipped = new Bitmap(Assets("badge.png"));
        Assert.Equal(chosen.Size, shipped.Size);

        int kept = 0, cleared = 0;
        for (int y = 0; y < chosen.Height; y += 3)
        {
            for (int x = 0; x < chosen.Width; x += 3)
            {
                var pixel = shipped.GetPixel(x, y);
                if (pixel.A == 255)
                {
                    kept++;
                    var original = chosen.GetPixel(x, y);
                    Assert.Equal((original.R, original.G, original.B), (pixel.R, pixel.G, pixel.B));
                }
                else if (pixel.A == 0) cleared++;
            }
        }
        Assert.True(kept > cleared, $"Only {kept} of its samples are the plate, against {cleared} cleared.");
        Assert.True(cleared > 0, "Nothing was cleared, so the black box would show on the faceplate.");
        Assert.Equal(0, shipped.GetPixel(0, 0).A);
    }

    [Fact]
    public void The_S_reaches_the_edges_of_the_largest_icon()
    {
        using var largest = LargestIconPicture();
        Assert.Equal(256, largest.Width);

        int left = 256, top = 256, right = -1, bottom = -1;
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                if (largest.GetPixel(x, y).A <= 16) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        }
        Assert.Equal(256, Math.Max(right - left, bottom - top) + 1);
        Assert.Equal(0, largest.GetPixel(0, 0).A);
    }

    static Bitmap LargestIconPicture()
    {
        byte[] icon = File.ReadAllBytes(Assets("stx.ico"));
        int count = BitConverter.ToUInt16(icon, 4);
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + 16 * i;
            if (icon[entry] != 0) continue;
            int size = BitConverter.ToInt32(icon, entry + 8), offset = BitConverter.ToInt32(icon, entry + 12);
            return new Bitmap(new MemoryStream(icon, offset, size));
        }
        throw new InvalidOperationException("The icon has no 256 pixel picture.");
    }
}
