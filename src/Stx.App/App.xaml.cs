using Microsoft.UI.Xaml;
using Stx.App.Gallery;

namespace Stx.App;

public partial class App : Application
{
    Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = GalleryOptions.Parse(Environment.GetCommandLineArgs());
        _window = new GalleryWindow(options);
        _window.Activate();
    }
}
