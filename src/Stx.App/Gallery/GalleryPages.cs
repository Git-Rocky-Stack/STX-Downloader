using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;

namespace Stx.App.Gallery;

/// <summary>Fixed pages for capture and baseline comparison, one per primitive.</summary>
internal static class GalleryPages
{
    public static UIElement Build(GalleryOptions options) => options.Name switch
    {
        "faceplate" => Faceplates(),
        _ => new Grid(),
    };

    static UIElement Faceplates()
    {
        var page = new Grid { RowSpacing = 8, Padding = new Thickness(8) };
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(380) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(96) });

        var large = new Faceplate { PanelName = "gallery-large" };
        var small = new Faceplate
        {
            PanelName = "gallery-small",
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetRow(small, 1);
        page.Children.Add(large);
        page.Children.Add(small);
        return page;
    }
}
