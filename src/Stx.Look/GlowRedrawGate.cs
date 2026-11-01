namespace Stx.Look;

/// <summary>Glow is rebuilt only when the text or its box changes, so an idle display does no work.</summary>
public sealed class GlowRedrawGate
{
    string? _text;
    double _width, _height;

    public int DrawCount { get; private set; }

    public bool ShouldRedraw(string text, double width, double height)
    {
        if (width <= 0 || height <= 0) return false;
        if (text == _text && width == _width && height == _height) return false;
        _text = text;
        _width = width;
        _height = height;
        DrawCount++;
        return true;
    }
}
