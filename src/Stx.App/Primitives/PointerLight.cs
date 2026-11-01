using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>Prototype: one composition sprite over the rack that follows the pointer. Moving it never touches the UI tree, and at rest it costs nothing.</summary>
public sealed class PointerLight
{
    readonly SpriteVisual _sprite;

    public PointerLight(UIElement overlay)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(overlay).Compositor;
        var brush = compositor.CreateRadialGradientBrush();
        var peak = Windows.UI.Color.FromArgb(PointerLightLayout.PeakAlpha(LookState.Mode), 255, 255, 255);
        var none = Windows.UI.Color.FromArgb(0, 255, 255, 255);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0, peak));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1, none));
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Size = new Vector2(PointerLightLayout.Width, PointerLightLayout.Height);
        _sprite.Brush = brush;
        _sprite.Opacity = 0;
        ElementCompositionPreview.SetElementChildVisual(overlay, _sprite);
    }

    public SpriteVisual Sprite => _sprite;

    public void Move(float x, float y)
    {
        var (left, top) = PointerLightLayout.OffsetFor(x, y);
        _sprite.Offset = new Vector3(left, top, 0);
        _sprite.Opacity = 1;
    }

    public void Hide() => _sprite.Opacity = 0;
}
