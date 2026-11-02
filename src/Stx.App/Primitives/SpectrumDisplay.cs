using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>A measured spectrum behind glass, with the measured roll-off marked. It redraws only when its data changes.</summary>
public sealed partial class SpectrumDisplay : ContentControl
{
    readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly SpectrumRedrawGate _gate = new();
    IReadOnlyList<double> _hz = [], _db = [];
    double? _rollOff;

    public SpectrumDisplay()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(8), Content = _image, VerticalContentAlignment = VerticalAlignment.Stretch };
        SizeChanged += (_, _) => Draw();
    }

    public void SetData(IReadOnlyList<double> hz, IReadOnlyList<double> db, double? rollOffHz)
    {
        _hz = hz;
        _db = db;
        _rollOff = rollOffHz;
        Draw();
    }

    void Draw()
    {
        double scale = XamlRoot?.RasterizationScale ?? 1;
        int width = (int)((ActualWidth - 28) * scale), height = (int)((ActualHeight - 28) * scale);
        if (!_gate.ShouldRedraw(_db, _rollOff, width, height)) return;

        var art = SpectrumArt.Render(width, height, _hz, _db, _rollOff, DisplayTone.Cyan);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(art.Bgra, 0, art.Bgra.Length);
        bitmap.Invalidate();
        _image.Source = bitmap;
    }
}
