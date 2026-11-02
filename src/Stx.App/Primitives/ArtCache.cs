using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Stx.Art;

namespace Stx.App.Primitives;

/// <summary>Identical parts share one bitmap. Art renders once, on first use.</summary>
public static class ArtCache
{
    static readonly Dictionary<string, WriteableBitmap> Bitmaps = new();

    public static WriteableBitmap Get(string key, Func<PixelBuffer> render)
    {
        if (Bitmaps.TryGetValue(key, out var existing)) return existing;

        var art = render();
        var bitmap = new WriteableBitmap(art.Width, art.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(art.Bgra, 0, art.Bgra.Length);
        bitmap.Invalidate();
        Bitmaps[key] = bitmap;
        return bitmap;
    }

    /// <summary>Drops bitmaps whose key starts with the prefix, such as resized plates no longer on screen.</summary>
    public static void Evict(string prefix)
    {
        foreach (var key in Bitmaps.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            Bitmaps.Remove(key);
    }
}
