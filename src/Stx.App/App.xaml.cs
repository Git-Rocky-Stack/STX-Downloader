using Microsoft.UI.Xaml;
using Stx.App.Gallery;

namespace Stx.App;

public partial class App : Application
{
    Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        _window = commandLine.Any(a => a.StartsWith("--gallery=", StringComparison.Ordinal))
            ? new GalleryWindow(GalleryOptions.Parse(commandLine))
            : new MainWindow();
        _window.Activate();
    }
}
