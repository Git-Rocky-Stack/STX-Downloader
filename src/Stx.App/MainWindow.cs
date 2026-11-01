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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 860));
        _app = new Composition(DispatcherQueue);

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(new HeaderPanel(_app.Status));
        rack.Children.Add(new SourcePanel(_app.Source));
        rack.Children.Add(new QueuePanel(_app.Queue));

        var root = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 8, 9, 10)) };
        root.Children.Add(new ScrollViewer { Content = rack });
        Content = root;

        root.Loaded += async (_, _) => await _app.StartAsync();
        Closed += (_, _) => _app.Dispose();
    }
}
