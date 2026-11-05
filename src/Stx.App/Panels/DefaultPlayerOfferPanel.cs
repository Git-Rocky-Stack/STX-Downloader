using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>First-run setup's one question about playing: asked once, and gone for good after YES or NOT NOW.</summary>
public sealed partial class DefaultPlayerOfferPanel : ContentControl
{
    public DefaultPlayerOfferPanel(DefaultPlayerViewModel player)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var yes = new Pad { Content = "YES" };
        var notNow = new Pad { Content = "NOT NOW" };
        yes.Click += (_, _) => player.YesCommand.Execute(null);
        notNow.Click += (_, _) => player.NotNowCommand.Execute(null);

        var pads = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pads.Children.Add(yes);
        pads.Children.Add(notNow);
        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Player" });
        column.Children.Add(new Display { Tone = DisplayTone.Cyan, Padding = new Thickness(12, 8, 12, 8), Content = new GlowText { Tone = DisplayTone.Cyan, Text = player.Question } });
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Yes opens Windows' Default Apps, where you choose STX. Windows doesn't let an app choose itself." });
        column.Children.Add(pads);
        Content = new Faceplate { PanelName = "default-player", Padding = new Thickness(36, 28, 36, 28), Content = column };

        void Paint() => Visibility = player.ShowOffer ? Visibility.Visible : Visibility.Collapsed;
        player.PropertyChanged += (_, _) => Paint();
        Paint();
    }
}
