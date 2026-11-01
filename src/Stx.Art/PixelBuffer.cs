using System.Security.Cryptography;

namespace Stx.Art;

/// <summary>Premultiplied BGRA pixels. Has no WinUI dependency, so art can be unit tested.</summary>
public sealed class PixelBuffer(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = new byte[width * height * 4];

    /// <summary>Writes a straight-alpha color, premultiplying it. Color values are clamped to 0-255.</summary>
    public void Set(int x, int y, double r, double g, double b, double a = 1)
    {
        a = Math.Clamp(a, 0, 1);
        int i = (y * Width + x) * 4;
        Bgra[i] = (byte)Math.Clamp(b * a, 0, 255);
        Bgra[i + 1] = (byte)Math.Clamp(g * a, 0, 255);
        Bgra[i + 2] = (byte)Math.Clamp(r * a, 0, 255);
        Bgra[i + 3] = (byte)(a * 255);
    }

    public int Alpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];

    public double Luma(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return Bgra[i] * 0.0722 + Bgra[i + 1] * 0.7152 + Bgra[i + 2] * 0.2126;
    }

    public double MeanLuma()
    {
        double sum = 0;
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) sum += Luma(x, y);
        return sum / (Width * Height);
    }

    public double MeanAbsDeltaHorizontal()
    {
        double sum = 0;
        for (int y = 0; y < Height; y++) for (int x = 1; x < Width; x++) sum += Math.Abs(Luma(x, y) - Luma(x - 1, y));
        return sum / ((Width - 1) * Height);
    }

    public double MeanAbsDeltaVertical()
    {
        double sum = 0;
        for (int y = 1; y < Height; y++) for (int x = 0; x < Width; x++) sum += Math.Abs(Luma(x, y) - Luma(x, y - 1));
        return sum / (Width * (Height - 1));
    }

    public string Hash() => Convert.ToHexString(SHA256.HashData(Bgra));
}
