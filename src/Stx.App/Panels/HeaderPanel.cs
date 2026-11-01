using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>The top plate: the name, and whether the tools are ready.</summary>
public sealed partial class HeaderPanel : ContentControl
{
    readonly Led _engine = new(), _fault = new() { Color = LedColor.Red };
    readonly EngravedLabel _engineText = new() { Kind = LabelKind.Label };

    public HeaderPanel(EngineStatusViewModel status, Action? openSettings = null)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var brand = new TextBlock { Text = "STX", FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Black, FontSize = 26, CharacterSpacing = 200 };
        LookState.Changed += () => brand.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);
        brand.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);
        var name = new StackPanel { Spacing = 2 };
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
        Content = new Faceplate { PanelName = "header", Padding = new Thickness(36, 22, 36, 22), Content = row };

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
