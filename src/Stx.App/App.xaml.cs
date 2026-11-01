using Microsoft.UI.Xaml;
using Stx.App.Gallery;
using Stx.App.Platform;

namespace Stx.App;

public partial class App : Application
{
    Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Any(a => a.StartsWith("--gallery=", StringComparison.Ordinal)))
        {
            _window = new GalleryWindow(GalleryOptions.Parse(commandLine));
            _window.Activate();
            return;
        }

        // A second launch just brings the first one forward.
        if (!SingleInstance.TryBecomeFirst())
        {
            Exit();
            return;
        }
        var main = new MainWindow(startHidden: commandLine.Contains(StartupRegistry.HiddenFlag));
        _window = main;
        SingleInstance.ListenForSecondLaunch(() => main.DispatcherQueue.TryEnqueue(main.ShowFromTray));
        if (!main.StartsHidden) main.Activate();
    }
}
