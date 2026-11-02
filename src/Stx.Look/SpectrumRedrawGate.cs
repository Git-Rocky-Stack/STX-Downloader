namespace Stx.Look;

/// <summary>A spectrum is redrawn only when its data, its marker or its size changes, so a selected item costs nothing at rest.</summary>
public sealed class SpectrumRedrawGate
{
    double[] _data = [];
    double? _mark;
    int _width, _height;

    public int DrawCount { get; private set; }

    public bool ShouldRedraw(IReadOnlyList<double> data, double? mark, int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        if (width == _width && height == _height && mark == _mark && data.SequenceEqual(_data)) return false;
        _data = data.ToArray();
        _mark = mark;
        _width = width;
        _height = height;
        DrawCount++;
        return true;
    }
}
