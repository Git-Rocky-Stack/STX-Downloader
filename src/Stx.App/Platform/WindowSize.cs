using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace Stx.App.Platform;

/// <summary>
/// Window sizes in device-independent pixels. STX is DPI-aware (app.manifest), so AppWindow takes physical pixels: a
/// size given at 96 DPI is scaled to the window's display, so the window looks the same size at any scale, and it's
/// kept inside the display's work area (a 1360x920 window at 150% is 2040x1380, more than a 1920x1080 screen holds).
/// A window left hanging off the work area is centred on it.
/// </summary>
internal static class WindowSize
{
    public static void Set(AppWindow window, int width, int height)
    {
        double scale = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(window.Id)) / 96.0;
        if (scale <= 0) scale = 1;
        var work = DisplayArea.GetFromWindowId(window.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Min((int)Math.Round(width * scale), work.Width);
        int h = Math.Min((int)Math.Round(height * scale), work.Height);
        window.Resize(new Windows.Graphics.SizeInt32(w, h));
        var at = window.Position;
        if (at.X < work.X || at.Y < work.Y || at.X + w > work.X + work.Width || at.Y + h > work.Y + work.Height)
            window.Move(new Windows.Graphics.PointInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2));
    }

    [DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr window);
}
