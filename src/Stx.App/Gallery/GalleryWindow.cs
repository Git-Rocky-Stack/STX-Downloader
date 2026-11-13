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
        // deck, tour and guide-wide are the console at the real window's own size (MainWindow), so its layout is the one
        // people see: the deck's columns meeting at the bottom, the guide's index beside its page, a tour's card placed.
        if (options.Name is "deck" or "tour" or "guide-wide") Platform.WindowSize.Set(AppWindow, 1360, 920);
        else Platform.WindowSize.Set(AppWindow, 960, options.Name is "settings" or "library" or "player-plate" ? 800 : 640);
        TitleStrip.Paint(AppWindow);
        var root = new Grid { Background = new SolidColorBrush(TitleStrip.Black) };
        root.Children.Add(options.Name.StartsWith("player", StringComparison.Ordinal) ? PlayerGallery.Build(this, options) : GalleryPages.Build(options));
        Content = root;
    }
}
