using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Stx.App.Primitives;

/// <summary>Turns token strings, #RRGGBB or #AARRGGBB, into brushes.</summary>
public static class LookBrushes
{
    public static Color Parse(string hex)
    {
        byte Part(int at) => Convert.ToByte(hex.Substring(at, 2), 16);
        return hex.Length == 9
            ? Color.FromArgb(Part(1), Part(3), Part(5), Part(7))
            : Color.FromArgb(255, Part(1), Part(3), Part(5));
    }

    public static SolidColorBrush From(string hex) => new(Parse(hex));
}
