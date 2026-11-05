using System.Security.Principal;
using Stx.Bridge;

namespace Stx.App.Platform;

/// <summary>
/// Only one app runs for each user. A second launch signals the first and exits, and waiting for that signal takes no
/// CPU. A second launch that brings a file (from File Explorer) hands it over instead, through OpenRequests.
/// </summary>
public static class SingleInstance
{
    static Mutex? _owned;
    static EventWaitHandle? _signal;
    static RegisteredWaitHandle? _wait;

    static string Name(string part) => $"Local\\STXDownloader-{WindowsIdentity.GetCurrent().User}-{part}";

    static OpenRequestServer? _opens;

    public static bool TryBecomeFirst(string? fileToOpen = null)
    {
        _owned = new Mutex(true, Name("app"), out bool first);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name("show"));
        if (first) return true;
        // The first copy plays the file and comes forward itself. If it can't take the file, it's at least shown.
        if (fileToOpen is null || !OpenRequests.SendAsync(OpenRequests.NameForCurrentUser(), fileToOpen, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())
            _signal.Set();
        return false;
    }

    /// <summary>Listens for files a later launch hands over. <paramref name="onFile"/> runs on a pool thread.</summary>
    public static void ListenForFiles(Action<string> onFile) =>
        _opens = new OpenRequestServer(OpenRequests.NameForCurrentUser(), onFile);

    public static void ListenForSecondLaunch(Action onSecondLaunch) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal!, (_, _) => onSecondLaunch(), null, Timeout.Infinite, executeOnlyOnce: false);
}
