using System.Drawing;
using Xunit;

namespace Stx.VisualTests;

/// <summary>The badge, icon and installer panel are the pictures Rocky chose. Nothing may cut, trim or recolor them.</summary>
public class BrandPictureTests
{
    // Scaling blurs fine detail by a little, and cutting the picture or moving it changes it by far more.
    const double MostAverageDifference = 8;

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Assets(string name) => Path.Combine(Root(), "src", "Stx.App", "Assets", name);

    static string Art(string name) => Path.Combine(Root(), "tools", "installer", "art", name);

    static string Chosen(string name) => Path.Combine(Root(), "icons", name);

    [Fact]
    public void The_badge_is_exactly_the_chosen_picture() =>
        Assert.Equal(File.ReadAllBytes(Chosen("stax badge.png")), File.ReadAllBytes(Assets("badge.png")));

    [Fact]
    public void The_largest_icon_is_the_chosen_picture_with_its_black()
    {
        using var largest = LargestIconPicture();
        Assert.Equal(256, largest.Width);
        AssertIsChosenIcon(largest, largest.Width, 0);
    }

    [Theory]
    [InlineData("corner-58.png")]
    [InlineData("corner-159.png")]
    public void The_installer_corner_is_the_chosen_picture_with_its_black(string picture)
    {
        using var corner = new Bitmap(Art(picture));
        AssertIsChosenIcon(corner, corner.Width, 0);
    }

    [Theory]
    [InlineData("panel-202.png")]
    [InlineData("panel-430.png")]
    public void The_installer_panel_is_the_whole_chosen_picture_on_black(string picture)
    {
        using var panel = new Bitmap(Art(picture));
        using var setup = new Bitmap(Chosen("setup.png"));
        double scale = Math.Min((double)panel.Width / setup.Width, (double)panel.Height / setup.Height);
        int width = (int)Math.Round(setup.Width * scale), height = (int)Math.Round(setup.Height * scale);
        using var onBlack = new Bitmap(width, height);
        using (var paint = Graphics.FromImage(onBlack))
        {
            paint.Clear(Color.Black);
            paint.DrawImage(setup, 0, 0, width, height);
        }
        AssertSamePicture(panel, onBlack, (panel.Width - width) / 2, (panel.Height - height) / 2);
        foreach (var (x, y) in new[] { (0, 0), (panel.Width - 1, 0), (0, panel.Height - 1), (panel.Width - 1, panel.Height - 1) })
            Assert.Equal((255, 0, 0, 0), (panel.GetPixel(x, y).A, panel.GetPixel(x, y).R, panel.GetPixel(x, y).G, panel.GetPixel(x, y).B));
    }

    static void AssertIsChosenIcon(Bitmap shipped, int size, int top)
    {
        using var chosen = new Bitmap(Chosen("stx final icon.png"));
        using var same = new Bitmap(chosen, new Size(size, size));
        AssertSamePicture(shipped, same, 0, top);
    }

    // Compares in blocks, so it's the picture being measured, and not how one scaler rounds a pixel against another.
    static void AssertSamePicture(Bitmap shipped, Bitmap wanted, int left, int top)
    {
        const int block = 4;
        double total = 0;
        int blocks = 0;
        for (int y = 0; y + block <= wanted.Height; y += block)
        {
            for (int x = 0; x + block <= wanted.Width; x += block)
            {
                double theirs = 0, ours = 0;
                for (int v = 0; v < block; v++)
                {
                    for (int u = 0; u < block; u++)
                    {
                        var pixel = shipped.GetPixel(x + u + left, y + v + top);
                        Assert.Equal(255, pixel.A);
                        ours += pixel.GetBrightness() * 255;
                        theirs += wanted.GetPixel(x + u, y + v).GetBrightness() * 255;
                    }
                }
                total += Math.Abs(theirs - ours) / (block * block);
                blocks++;
            }
        }
        double average = total / blocks;
        Assert.True(average < MostAverageDifference, $"On average {average:F1} levels away from the chosen picture.");
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
