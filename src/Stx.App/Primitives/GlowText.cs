using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Stx.Look;
using Windows.UI;

namespace Stx.App.Primitives;

/// <summary>
/// Display text with a real glow taken from the text's own alpha mask.
/// The inner text must hug its content. If it stretches, the mask is scaled to the wrong box and smears.
/// </summary>
public sealed partial class GlowText : Grid
{
    readonly Grid _glowHost = new() { IsHitTestVisible = false };
    readonly TextBlock _text = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        FontSize = 15,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };
    readonly GlowRedrawGate _gate = new();
    CompositionBrush? _mask;
    DisplayTone _tone = DisplayTone.Amber;
    bool _dimmed;

    public GlowText()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(_glowHost);
        Children.Add(_text);
        _text.SizeChanged += (_, _) => RefreshGlow();
        ApplyColor();
    }

    public string Text
    {
        get => _text.Text;
        set { _text.Text = value; RefreshGlow(); }
    }

    public double TextSize
    {
        get => _text.FontSize;
        set => _text.FontSize = value;
    }

    public DisplayTone Tone
    {
        get => _tone;
        set { _tone = value; ApplyColor(); RebuildGlow(); }
    }

    public bool Dimmed
    {
        get => _dimmed;
        set { _dimmed = value; ApplyColor(); RebuildGlow(); }
    }

    void ApplyColor() => _text.Foreground = new SolidColorBrush(CurrentColor());

    Color CurrentColor()
    {
        string hex = DisplayTones.Text(_tone, _dimmed);
        return Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }

    void RefreshGlow()
    {
        if (_gate.ShouldRedraw(_text.Text, _text.ActualWidth, _text.ActualHeight)) RebuildGlow();
    }

    void RebuildGlow()
    {
        if (_text.ActualWidth <= 0 || _text.ActualHeight <= 0) return;

        var compositor = ElementCompositionPreview.GetElementVisual(_glowHost).Compositor;
        _mask ??= _text.GetAlphaMask();
        var stack = compositor.CreateContainerVisual();
        var size = new Vector2((float)_text.ActualWidth, (float)_text.ActualHeight);
        var color = CurrentColor();
        int layers = _dimmed ? 2 : DisplayTones.GlowBlurs.Count;

        for (int i = 0; i < layers; i++)
        {
            var shadow = compositor.CreateDropShadow();
            shadow.Color = color;
            shadow.BlurRadius = DisplayTones.GlowBlurs[i];
            shadow.Opacity = DisplayTones.GlowOpacities[i];
            shadow.Mask = _mask;
            var sprite = compositor.CreateSpriteVisual();
            sprite.Size = size;
            sprite.Shadow = shadow;
            stack.Children.InsertAtTop(sprite);
        }
        ElementCompositionPreview.SetElementChildVisual(_glowHost, stack);
    }
}
