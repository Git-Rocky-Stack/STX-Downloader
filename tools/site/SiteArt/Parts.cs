using Stx.Art;
using Stx.Look;

namespace Stx.SiteArt;

/// <summary>One image the page uses. <paramref name="Render"/> takes the scale (1 or 2) and returns LogicalWidth*scale by LogicalHeight*scale pixels.</summary>
public sealed record Part(string Name, int LogicalWidth, int LogicalHeight, Func<int, PixelBuffer> Render);

/// <summary>The page's hardware, drawn by the art code the app draws its own with, so the two match pixel for pixel.</summary>
public static class Parts
{
    const int LevelSegments = 32, ProgressSegments = 24;

    // Pads are drawn at exact size, never stretched: PadFace shades across its width, and the app draws each pad at its own size.
    // The page gives each pad the smallest class its label fits in. Sizes are the face, in logical px.
    static readonly (string Name, int Width, int Height)[] PadClasses = [("s", 96, 40), ("m", 128, 52), ("l", 176, 52), ("xl", 240, 72)];

    // The grain's gain. 128 + (grain + row offset) * gain has a luma standard deviation of about 7.2, which is the
    // approved mockup's tile (docs/design/site-v2/grain.webp measures 7.16). The light tile uses the same gain.
    const double GrainGain = 3.49;

    static readonly (string Name, LedColor Color)[] LedColors =
    [
        ("green", LedColor.Green), ("red", LedColor.Red), ("amber", LedColor.Amber), ("blue", LedColor.Blue),
    ];

    public static IReadOnlyList<Part> All()
    {
        int led = (int)LedArt.LedArtLogical;
        int segW = (int)LedArt.SegmentArtWidth, segH = (int)LedArt.SegmentArtHeight;
        int digitW = (int)(ControlArt.DigitWidth + 2 * ControlArt.DigitGlow), digitH = (int)(ControlArt.DigitHeight + 2 * ControlArt.DigitGlow);
        int well = (int)ControlArt.WellInset * 2, bloom = (int)ControlArt.PadBloomOverhang * 2;
        var parts = new List<Part>
        {
            new("grain-dark", 512, 256, s => PlateArt.GrainTile(512 * s, 256 * s, LookMode.Dark, GrainGain)),
            new("grain-light", 512, 256, s => PlateArt.GrainTile(512 * s, 256 * s, LookMode.Light, GrainGain)),
        };

        (string Name, int Rotation)[] screws = [("a", 7), ("b", 22), ("c", 38), ("d", 51)];
        foreach (var (name, rotation) in screws)
            parts.Add(new($"screw-{name}", 18, 18, s => ScrewArt.Render(18 * s, rotation)));

        parts.Add(new("led-housing", led, led, s => LedArt.Housing(led * s)));
        foreach (var (name, color) in LedColors) parts.Add(new($"led-lit-{name}", led, led, s => LedArt.Lit(color, led * s)));
        foreach (var (name, color) in LedColors) parts.Add(new($"led-flare-{name}", led, led, s => LedArt.Flare(color, led * s)));

        parts.Add(Strip("seg-level-lit", LevelSegments, segW, segH, (i, s) => LedArt.SegmentLit(ZoneHex(i, LevelSegments, BargraphZones.Vu), segW * s, segH * s)));
        parts.Add(Strip("seg-level-unlit", LevelSegments, segW, segH, (i, s) => UnlitCell(ZoneHex(i, LevelSegments, BargraphZones.Vu), s)));
        parts.Add(Strip("seg-level-peak", LevelSegments, segW, segH, (i, s) => LedArt.SegmentPeak(ZoneHex(i, LevelSegments, BargraphZones.Vu), segW * s, segH * s)));
        parts.Add(Strip("seg-progress-lit", ProgressSegments, segW, segH, (i, s) => LedArt.SegmentLit(ZoneHex(i, ProgressSegments, BargraphZones.Progress), segW * s, segH * s)));
        parts.Add(Strip("seg-progress-unlit", ProgressSegments, segW, segH, (i, s) => UnlitCell(ZoneHex(i, ProgressSegments, BargraphZones.Progress), s)));

        // 0-9, then a cell no segment is lit in, for the positions a readout leaves dark.
        const string Glyphs = "0123456789 ";
        parts.Add(Strip("digits", Glyphs.Length, digitW, digitH, (i, s) => ControlArt.Digit(Glyphs[i], digitW * s, digitH * s)));

        foreach (var (c, w, h) in PadClasses)
        {
            parts.Add(new($"pad-{c}-well-dark", w + well, h + well, s => ControlArt.PadWell((w + well) * s, (h + well) * s, s, LookMode.Dark)));
            parts.Add(new($"pad-{c}-well-light", w + well, h + well, s => ControlArt.PadWell((w + well) * s, (h + well) * s, s, LookMode.Light)));
            foreach (var (name, tone) in new[] { ("off", PadTone.Off), ("green", PadTone.Green), ("blue", PadTone.Blue) })
                parts.Add(new($"pad-{c}-face-{name}", w, h, s => ControlArt.PadFace(w * s, h * s, s, tone)));
            parts.Add(new($"pad-{c}-press", w, h, s => ControlArt.PadPressShade(w * s, h * s, s)));
            foreach (var (name, tone) in new[] { ("green", PadTone.Green), ("blue", PadTone.Blue) })
                parts.Add(new($"pad-{c}-bloom-{name}", w + bloom, h + bloom, s => ControlArt.PadBloom((w + bloom) * s, (h + bloom) * s, s, tone)));
        }
        return parts;
    }

    static string ZoneHex(int index, int segments, BargraphZones zones) => BargraphLogic.ZoneHex(index, segments, zones);

    /// <summary>A dead segment has no overhang, so it is drawn at its core size and centred in a full-size cell, as the app lays it.</summary>
    static PixelBuffer UnlitCell(string hex, int scale)
    {
        int coreW = (int)LedArt.SegmentWidth * scale, coreH = (int)LedArt.SegmentHeight * scale;
        var cell = new PixelBuffer((int)LedArt.SegmentArtWidth * scale, (int)LedArt.SegmentArtHeight * scale);
        Blit(cell, LedArt.SegmentUnlit(hex, coreW, coreH), (cell.Width - coreW) / 2, (cell.Height - coreH) / 2);
        return cell;
    }

    /// <summary>Cells laid side by side, left to right.</summary>
    static Part Strip(string name, int count, int cellW, int cellH, Func<int, int, PixelBuffer> cell) =>
        new(name, cellW * count, cellH, s =>
        {
            var strip = new PixelBuffer(cellW * count * s, cellH * s);
            for (int i = 0; i < count; i++) Blit(strip, cell(i, s), i * cellW * s, 0);
            return strip;
        });

    static void Blit(PixelBuffer target, PixelBuffer source, int x0, int y0)
    {
        for (int y = 0; y < source.Height; y++)
            Buffer.BlockCopy(source.Bgra, y * source.Width * 4, target.Bgra, ((y0 + y) * target.Width + x0) * 4, source.Width * 4);
    }
}
