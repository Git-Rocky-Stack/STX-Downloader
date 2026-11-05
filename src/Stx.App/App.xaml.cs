using Microsoft.UI.Xaml;
using Stx.App.Gallery;
using Stx.App.Platform;
using Stx.Core.Playback;

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

        // A file from File Explorer ("Open with", or a double-click once STX is the default player).
        string? file = LaunchArgs.FileToOpen(commandLine.Skip(1).ToList(), File.Exists);

        // A second launch brings the first one forward, handing it the file if there is one.
        if (!SingleInstance.TryBecomeFirst(file))
        {
            Exit();
            return;
        }
        var main = new MainWindow(startHidden: commandLine.Contains(StartupRegistry.HiddenFlag), pointerLight: commandLine.Contains("--pointer-light"));
        _window = main;
        SingleInstance.ListenForSecondLaunch(() => main.DispatcherQueue.TryEnqueue(main.ShowFromTray));
        SingleInstance.ListenForFiles(path => main.DispatcherQueue.TryEnqueue(() => main.OpenFromOutside(path)));
        if (file is not null) main.DispatcherQueue.TryEnqueue(() => main.OpenFromOutside(file));
        else if (!main.StartsHidden) main.Activate();
    }
}
