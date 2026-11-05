using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Stx.VisualTests;

public readonly record struct DiffResult(bool Pass, double ChangedRatio);

public static class ScreenshotDiff
{
    const int ChannelTolerance = 4;
    const double MaxChangedRatio = 0.005;

    public static DiffResult Compare(string actualPng, string baselinePng)
    {
        using var a = new Bitmap(actualPng);
        using var b = new Bitmap(baselinePng);
        if (a.Size != b.Size) return new DiffResult(false, 1);

        var pa = Read(a);
        var pb = Read(b);
        long changed = 0;
        for (int i = 0; i < pa.Length; i += 4)
        {
            if (Math.Abs(pa[i] - pb[i]) > ChannelTolerance
                || Math.Abs(pa[i + 1] - pb[i + 1]) > ChannelTolerance
                || Math.Abs(pa[i + 2] - pb[i + 2]) > ChannelTolerance)
                changed++;
        }
        double ratio = changed / (double)(a.Width * a.Height);
        return new DiffResult(ratio <= MaxChangedRatio, ratio);
    }

    /// <summary>True when every pixel is pure black: what PrintWindow returns for a window that hasn't drawn yet. Neither mode's background is pure black.</summary>
    public static bool IsBlank(string png)
    {
        using var bmp = new Bitmap(png);
        var p = Read(bmp);
        for (int i = 0; i < p.Length; i += 4)
            if (p[i] != 0 || p[i + 1] != 0 || p[i + 2] != 0) return false;
        return true;
    }

    static byte[] Read(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bmp.Width * bmp.Height * 4];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bmp.Width * 4, bmp.Width * 4);
            return bytes;
        }
        finally { bmp.UnlockBits(data); }
    }
}
