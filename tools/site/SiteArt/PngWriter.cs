using System.Buffers.Binary;
using System.IO.Compression;
using Stx.Art;

namespace Stx.SiteArt;

/// <summary>Writes a <see cref="PixelBuffer"/> as an 8-bit RGBA PNG. The same buffer always gives the same bytes.</summary>
public static class PngWriter
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static byte[] Encode(PixelBuffer buffer)
    {
        using var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), buffer.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), buffer.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // color type: RGBA
        WriteChunk(png, "IHDR", header);

        // Every scanline is filter 0 (none) followed by its RGBA pixels, un-premultiplied.
        var raw = new byte[buffer.Height * (buffer.Width * 4 + 1)];
        int o = 0;
        for (int y = 0; y < buffer.Height; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < buffer.Width; x++)
            {
                int i = (y * buffer.Width + x) * 4;
                int a = buffer.Bgra[i + 3];
                raw[o++] = Straight(buffer.Bgra[i + 2], a);
                raw[o++] = Straight(buffer.Bgra[i + 1], a);
                raw[o++] = Straight(buffer.Bgra[i], a);
                raw[o++] = (byte)a;
            }
        }

        using var compressed = new MemoryStream();
        // A fixed level, so the bytes only change when the art does.
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(raw);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    static byte Straight(byte premultiplied, int alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, (premultiplied * 255 + alpha / 2) / alpha);

    static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var body = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        stream.Write(body);

        uint crc = 0xFFFFFFFF;
        foreach (byte b in body) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        stream.Write(crcBytes);
    }
}
