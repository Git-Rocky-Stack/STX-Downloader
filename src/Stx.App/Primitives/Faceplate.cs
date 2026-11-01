using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>A brushed aluminium panel. Screws are added in code, so a panel cannot exist without them.</summary>
public sealed partial class Faceplate : ContentControl
{
    const int ScrewArtSize = 72;

    Image? _plate;
    Canvas? _screws;

    public Faceplate()
    {
        DefaultStyleKey = typeof(Faceplate);
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => { LookState.Changed += Redraw; Redraw(); };
        Unloaded += (_, _) => LookState.Changed -= Redraw;
    }

    /// <summary>Gives this panel's screws their own stable seat angles.</summary>
    public string PanelName { get; set; } = "panel";

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _plate = GetTemplateChild("PART_Plate") as Image;
        _screws = GetTemplateChild("PART_Screws") as Canvas;
        Redraw();
    }

    void Redraw()
    {
        if (_plate is null || _screws is null || ActualWidth < 1 || ActualHeight < 1) return;

        double scale = XamlRoot?.RasterizationScale ?? 1;
        int w = (int)Math.Ceiling(ActualWidth * scale), h = (int)Math.Ceiling(ActualHeight * scale);
        _plate.Width = ActualWidth;
        _plate.Height = ActualHeight;
        var mode = LookState.Mode;
        _plate.Source = ArtCache.Get($"plate:{mode}:{w}x{h}", () => PlateArt.Render(w, h, mode));

        _screws.Children.Clear();
        var spots = ScrewLayout.For(ActualWidth, ActualHeight);
        var angles = ScrewLayout.AnglesFor(PanelName);
        for (int i = 0; i < spots.Count; i++)
        {
            int angle = angles[i];
            var screw = new Image
            {
                Width = ScrewLayout.Size,
                Height = ScrewLayout.Size,
                IsHitTestVisible = false,
                Source = ArtCache.Get($"screw:{angle}", () => ScrewArt.Render(ScrewArtSize, angle)),
            };
            Canvas.SetLeft(screw, spots[i].X);
            Canvas.SetTop(screw, spots[i].Y);
            _screws.Children.Add(screw);
        }
    }
}
