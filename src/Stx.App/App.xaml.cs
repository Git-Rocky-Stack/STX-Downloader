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
            var options = GalleryOptions.Parse(commandLine);
            _window = new GalleryWindow(options);
            if (options.Hold) _window.AppWindow.Show(activateWindow: false);
            else _window.Activate();
            return;
        }

        // A second launch just brings the first one forward.
        if (!SingleInstance.TryBecomeFirst())
        {
            Exit();
            return;
        }
        var main = new MainWindow(startHidden: commandLine.Contains(StartupRegistry.HiddenFlag), pointerLight: commandLine.Contains("--pointer-light"));
        _window = main;
        SingleInstance.ListenForSecondLaunch(() => main.DispatcherQueue.TryEnqueue(main.ShowFromTray));
        if (!main.StartsHidden) main.Activate();
    }
}
