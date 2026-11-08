using Microsoft.UI.Xaml;
using Stx.App.Gallery;
using Stx.App.Platform;
using Stx.Core.Logging;
using Stx.Core.Playback;

namespace Stx.App;

public partial class App : Application
{
    Window? _window;

    public App()
    {
        InitializeComponent();
        // Review 2026-11-07: nothing listened for what escaped, so a crash left no trace, and a faulted background task
        // vanished. Everything that escapes now reaches the error log first.
        UnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            ErrorLog.Write("crash", e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString()));
            // The process is ending: the tray icon goes now, not when the pointer next passes over its ghost.
            try { Fatal?.Invoke(); } catch (Exception) { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("background task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Raised as the process goes down on an exception nothing caught. Off the UI thread; best effort only.</summary>
    internal static event Action? Fatal;

    // On the UI thread, from an event handler or a binding. The console is kept going, and says it recovered: one
    // handler's failure isn't worth closing a download queue and a film over.
    void OnUnhandled(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("ui", e.Exception);
        e.Handled = true;
        (_window as MainWindow)?.ReportRecovered();
    }

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
