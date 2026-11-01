using System.Numerics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Stx.Art;
using Stx.Look;
using Windows.UI;

namespace Stx.App.Primitives;

/// <summary>
/// A ball-tipped toggle through a hex nut. It is a real ToggleButton, so keyboard and screen readers come with it.
/// The lever swings on a composition animation, overshooting unless motion is reduced.
/// </summary>
public sealed partial class Toggle : ToggleButton
{
    const double HardwareWidth = 40, HardwareHeight = 62, NutSize = 40, CollarSize = 26;
    const float SwingSeconds = 0.11f, UpAngle = 180;

    Microsoft.UI.Composition.Visual? _leverVisual;

    public Toggle()
    {
        DefaultStyleKey = typeof(Toggle);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Checked += (_, _) => Swing(animate: true);
        Unchecked += (_, _) => Swing(animate: true);
        IsEnabledChanged += (_, _) => PaintLook();
        Loaded += (_, _) => { LookState.Changed += PaintLook; PaintLook(); };
        Unloaded += (_, _) => LookState.Changed -= PaintLook;
        PaintLook();
    }

    /// <summary>The label follows the mode, not the Windows theme. A disabled switch reads as dimmed hardware.</summary>
    void PaintLook()
    {
        string hex = LookTokens.For(LookState.Mode)["Ink"];
        Foreground = new SolidColorBrush(Color.FromArgb(255, Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)));
        Opacity = IsEnabled ? 1 : 0.5;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Hardware") is not Canvas host) return;

        host.Children.Clear();
        double centerX = HardwareWidth / 2, centerY = HardwareHeight / 2;
        host.Children.Add(Part(ArtCache.Get("toggle:nut", () => ControlArt.Nut(120)), centerX - NutSize / 2, centerY - NutSize / 2, NutSize, NutSize));
        host.Children.Add(Part(ArtCache.Get("toggle:collar", () => ControlArt.Collar(78)), centerX - CollarSize / 2, centerY - CollarSize / 2, CollarSize, CollarSize));

        var lever = Part(ArtCache.Get("toggle:lever", () => ControlArt.Lever(48, 96)), centerX - ControlArt.LeverWidth / 2, centerY, ControlArt.LeverWidth, ControlArt.LeverHeight);
        host.Children.Add(lever);
        _leverVisual = ElementCompositionPreview.GetElementVisual(lever);
        _leverVisual.CenterPoint = new Vector3((float)(ControlArt.LeverWidth / 2), 0, 0);
        Swing(animate: false);
    }

    static Image Part(Microsoft.UI.Xaml.Media.ImageSource source, double left, double top, double width, double height)
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill, IsHitTestVisible = false };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        return image;
    }

    void Swing(bool animate)
    {
        if (_leverVisual is null) return;
        float angle = IsChecked == true ? UpAngle : 0;
        if (!animate)
        {
            _leverVisual.RotationAngleInDegrees = angle;
            return;
        }

        var compositor = _leverVisual.Compositor;
        var (x1, y1, x2, y2) = PressTravel.TurnEasing(LookState.ReduceMotion);
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
        var swing = compositor.CreateScalarKeyFrameAnimation();
        swing.InsertKeyFrame(1, angle, easing);
        swing.Duration = TimeSpan.FromSeconds(SwingSeconds);
        _leverVisual.StartAnimation("RotationAngleInDegrees", swing);
    }
}
