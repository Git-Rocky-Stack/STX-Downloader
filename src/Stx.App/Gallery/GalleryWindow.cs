using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Stx.App.Gallery;

/// <summary>Shows one primitive at a fixed size so it can be captured and compared with a baseline.</summary>
public sealed class GalleryWindow : Window
{
    public GalleryWindow(GalleryOptions options)
    {
        Title = $"STX gallery {options.Name} {options.Mode}";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 640));
        Content = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
    }
}
