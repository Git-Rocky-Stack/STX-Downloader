namespace Stx.Art;

/// <summary>The lens over a display: a diagonal reflection top left, a faint second band, and light along the top edge.</summary>
public static class GlassArt
{
    public static PixelBuffer Render(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double diagonal = x + y * 0.47, alpha = 0;
                if (diagonal < width * 0.18) alpha = 0.1 - 0.05 * diagonal / (width * 0.18);
                else if (diagonal > width * 0.62 && diagonal < width * 0.8) alpha = 0.035;

                double topEdge = 0.11 * (1 - y / (height * 0.14));
                if (topEdge > 0) alpha += topEdge;

                if (alpha > 0) buffer.Set(x, y, 255, 255, 255, alpha);
            }
        }
        return buffer;
    }
}
