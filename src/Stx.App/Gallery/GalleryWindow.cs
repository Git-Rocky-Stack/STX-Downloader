using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;

namespace Stx.App.Gallery;

/// <summary>Shows one primitive at a fixed size so it can be captured and compared with a baseline.</summary>
public sealed partial class GalleryWindow : Window
{
    public GalleryWindow(GalleryOptions options)
    {
        LookState.Mode = options.Mode;
        LookState.ReduceMotionChosen = options.ReduceMotion;
        Title = $"STX gallery {options.Name} {options.Mode}";
        // Settings and the Library grew in Phases 2 to 7 (watched folders, BIT-PERFECT, Shrink) past the first 640, and
        // player-plate holds the ENHANCE knob and its status under the pads.
        Platform.WindowSize.Set(AppWindow, 960, options.Name is "settings" or "library" or "player-plate" ? 800 : 640);
        TitleStrip.Paint(AppWindow);
        var root = new Grid { Background = new SolidColorBrush(TitleStrip.Black) };
        root.Children.Add(options.Name.StartsWith("player", StringComparison.Ordinal) ? PlayerGallery.Build(this, options) : GalleryPages.Build(options));
        Content = root;
    }
}
