using System.Drawing;

namespace Stx.VisualTests;

/// <summary>
/// A region of a gallery capture, given as it is in the 944-pixel-wide capture a 96 DPI screen makes. STX is DPI-aware
/// (2026-11-07), so on a screen scaled to 175% the same gallery captures 1652 pixels wide; the region is scaled to the
/// capture it's read from, so a test looks at the same part of the picture at any scale.
/// </summary>
static class CaptureArea
{
    public const int Width96 = 944;

    public static Rectangle In(Rectangle at96, Bitmap capture)
    {
        double scale = capture.Width / (double)Width96;
        return new Rectangle((int)(at96.X * scale), (int)(at96.Y * scale), (int)(at96.Width * scale), (int)(at96.Height * scale));
    }
}
