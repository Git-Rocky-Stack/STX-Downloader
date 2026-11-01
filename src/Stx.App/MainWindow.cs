using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Panels;
using Stx.App.Primitives;

namespace Stx.App;

/// <summary>The console: everything the app does, on one rack of plates.</summary>
public sealed partial class MainWindow : Window
{
    readonly Composition _app;

    public MainWindow()
    {
        Title = "STX Downloader";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 920));
        _app = new Composition(DispatcherQueue);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(_app.Status));
        rack.Children.Add(new SourcePanel(_app.Source));
        var deck = new Grid { ColumnSpacing = 8 };
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        deck.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var truth = new SourceTruthPanel(_app.SourceTruth, _app.ConvertSelected);
        Grid.SetColumn(truth, 1);
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(new QueuePanel(_app.Queue));
        left.Children.Add(new LibraryPanel(_app.Library, _app.Select));
        deck.Children.Add(left);
        deck.Children.Add(truth);
        rack.Children.Add(deck);

        var root = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
        root.Children.Add(new ScrollViewer { Content = rack });
        Content = root;

        root.Loaded += async (_, _) => await _app.StartAsync();
        Closed += (_, _) => _app.Dispose();
    }
}
