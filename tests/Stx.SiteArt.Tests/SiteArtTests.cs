using System.Buffers.Binary;
using Stx.Art;
using Stx.Look;
using Xunit;

namespace Stx.SiteArt.Tests;

public class SiteArtTests
{
    static readonly string[] ExpectedNames =
    [
        "grain-dark", "grain-light",
        "screw-a", "screw-b", "screw-c", "screw-d",
        "led-housing",
        "led-lit-green", "led-lit-red", "led-lit-amber", "led-lit-blue",
        "led-flare-green", "led-flare-red", "led-flare-amber", "led-flare-blue",
        "seg-level-lit", "seg-level-unlit", "seg-level-peak",
        "seg-progress-lit", "seg-progress-unlit",
        "digits",
        "pad-s-well-dark", "pad-s-well-light", "pad-s-face-off", "pad-s-face-green", "pad-s-face-blue", "pad-s-press", "pad-s-bloom-green", "pad-s-bloom-blue",
        "pad-m-well-dark", "pad-m-well-light", "pad-m-face-off", "pad-m-face-green", "pad-m-face-blue", "pad-m-press", "pad-m-bloom-green", "pad-m-bloom-blue",
        "pad-l-well-dark", "pad-l-well-light", "pad-l-face-off", "pad-l-face-green", "pad-l-face-blue", "pad-l-press", "pad-l-bloom-green", "pad-l-bloom-blue",
        "pad-xl-well-dark", "pad-xl-well-light", "pad-xl-face-off", "pad-xl-face-green", "pad-xl-face-blue", "pad-xl-press", "pad-xl-bloom-green", "pad-xl-bloom-blue",
    ];

    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Walks a PNG's chunks and returns the first IDAT's decompressed scanlines, filter bytes stripped.</summary>
    static (int W, int H, byte[] Rgba) Decode(byte[] png)
    {
        int w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        using var idat = new MemoryStream();
        for (int pos = 8; pos < png.Length;)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos, 4));
            if (System.Text.Encoding.ASCII.GetString(png, pos + 4, 4) == "IDAT") idat.Write(png, pos + 8, len);
            pos += 12 + len;
        }
        idat.Position = 0;
        using var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var rows = raw.ToArray();
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(0, rows[y * (w * 4 + 1)]);
            Array.Copy(rows, y * (w * 4 + 1) + 1, rgba, y * w * 4, w * 4);
        }
        return (w, h, rgba);
    }

    [Fact]
    public void Png_round_trips_an_opaque_pixel()
    {
        var buffer = new PixelBuffer(2, 1);
        buffer.Set(0, 0, 10, 20, 30);
        buffer.Set(1, 0, 200, 100, 50);

        var png = PngWriter.Encode(buffer);

        Assert.Equal(Signature, png[..8]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.Equal(8, png[24]);
        Assert.Equal(6, png[25]);
        var (_, _, rgba) = Decode(png);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 200, 100, 50, 255 }, rgba);
    }

    [Fact]
    public void Png_unpremultiplies()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.Set(0, 0, 200, 100, 50, 0.5);

        var (_, _, rgba) = Decode(PngWriter.Encode(buffer));

        Assert.InRange(rgba[0], 199, 201);
        Assert.InRange(rgba[1], 99, 101);
        Assert.InRange(rgba[2], 49, 51);
        Assert.InRange(rgba[3], 127, 129);
    }

    [Fact]
    public void Every_part_renders_at_both_scales_at_its_logical_size_times_scale()
    {
        foreach (var part in Parts.All())
            foreach (int scale in new[] { 1, 2 })
            {
                var buffer = part.Render(scale);
                Assert.True(buffer.Width == part.LogicalWidth * scale && buffer.Height == part.LogicalHeight * scale,
                    $"{part.Name}@{scale}x is {buffer.Width}x{buffer.Height}, wanted {part.LogicalWidth * scale}x{part.LogicalHeight * scale}");
            }
    }

    [Fact]
    public void Part_names_are_unique_and_match_the_plan()
    {
        var names = Parts.All().Select(p => p.Name).ToArray();
        Assert.Equal(ExpectedNames, names);
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Pad_classes_have_the_planned_face_sizes()
    {
        var sizes = Parts.All().ToDictionary(p => p.Name, p => (p.LogicalWidth, p.LogicalHeight));
        int well = (int)ControlArt.WellInset * 2, bloom = (int)ControlArt.PadBloomOverhang * 2;
        foreach (var (c, w, h) in new[] { ("s", 96, 40), ("m", 128, 52), ("l", 176, 52), ("xl", 240, 72) })
        {
            Assert.Equal((w, h), sizes[$"pad-{c}-face-off"]);
            Assert.Equal((w + well, h + well), sizes[$"pad-{c}-well-dark"]);
            Assert.Equal((w + bloom, h + bloom), sizes[$"pad-{c}-bloom-green"]);
        }
    }

    [Fact]
    public void Grain_dark_texture_matches_the_mockup()
    {
        var tile = Parts.All().Single(p => p.Name == "grain-dark").Render(1);
        double mean = tile.MeanLuma(), sum = 0;
        for (int y = 0; y < tile.Height; y++)
            for (int x = 0; x < tile.Width; x++) sum += Math.Pow(tile.Luma(x, y) - mean, 2);
        double std = Math.Sqrt(sum / (tile.Width * tile.Height));
        Assert.InRange(std, 7.2 - 0.5, 7.2 + 0.5);
    }

    [Fact]
    public void Check_mode_reports_a_changed_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "siteart-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, Program.Run(["--out", dir], new StringWriter()));
            Assert.Equal(0, Program.Run(["--check", dir], new StringWriter()));

            var file = Path.Combine(dir, "screw-a@2x.png");
            var bytes = File.ReadAllBytes(file);
            bytes[^20] ^= 0xFF;
            File.WriteAllBytes(file, bytes);

            var output = new StringWriter();
            Assert.Equal(1, Program.Run(["--check", dir], output));
            Assert.Contains("screw-a@2x.png", output.ToString());
            Assert.DoesNotContain("screw-b@2x.png", output.ToString());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
