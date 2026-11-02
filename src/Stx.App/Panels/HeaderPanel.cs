using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>The top plate: the name, and whether the tools are ready.</summary>
public sealed partial class HeaderPanel : ContentControl
{
    const double BadgeHeight = 41;

    readonly Led _engine = new(), _fault = new() { Color = LedColor.Red };
    readonly EngravedLabel _engineText = new() { Kind = LabelKind.Label };

    public HeaderPanel(EngineStatusViewModel status, Action? openSettings = null)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var brand = new Image
        {
            // Scaled once, on loading, to the exact pixels this display needs. The picture is 2521 wide, and shrinking that live blurs its fine detail.
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "badge.png")))
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelHeight = (int)BadgeHeight,
            },
            Height = BadgeHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(brand, "STX-1");
        var name = new StackPanel { Spacing = 6 };
        name.Children.Add(brand);
        name.Children.Add(new EngravedLabel { Kind = LabelKind.Sub, Text = "Media capture console" });

        var lights = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        lights.Children.Add(Pair(_engine, _engineText));
        lights.Children.Add(Pair(_fault, new EngravedLabel { Kind = LabelKind.Label, Text = "Fault" }));
        if (openSettings is not null)
        {
            var settings = new Pad { Content = "SETTINGS" };
            settings.Click += (_, _) => openSettings();
            lights.Children.Add(settings);
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(lights, 1);
        row.Children.Add(name);
        row.Children.Add(lights);
        Content = new Faceplate { PanelName = "header", Padding = new Thickness(36, 18, 36, 19), Content = row };

        status.PropertyChanged += (_, _) => Paint(status);
        Paint(status);
    }

    static StackPanel Pair(UIElement led, UIElement label)
    {
        var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        pair.Children.Add(led);
        pair.Children.Add(label);
        return pair;
    }

    void Paint(EngineStatusViewModel status)
    {
        _engine.Color = status.EngineColor;
        _engine.State = status.EngineState;
        _fault.State = status.FaultState;
        _engineText.Text = status.EngineText;
    }
}
