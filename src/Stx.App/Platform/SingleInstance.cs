using System.Security.Principal;

namespace Stx.App.Platform;

/// <summary>Only one app runs for each user. A second launch signals the first and exits, and waiting for that signal takes no CPU.</summary>
public static class SingleInstance
{
    static Mutex? _owned;
    static EventWaitHandle? _signal;
    static RegisteredWaitHandle? _wait;

    static string Name(string part) => $"Local\\STXDownloader-{WindowsIdentity.GetCurrent().User}-{part}";

    public static bool TryBecomeFirst()
    {
        _owned = new Mutex(true, Name("app"), out bool first);
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name("show"));
        if (first) return true;
        _signal.Set();
        return false;
    }

    public static void ListenForSecondLaunch(Action onSecondLaunch) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal!, (_, _) => onSecondLaunch(), null, Timeout.Infinite, executeOnlyOnce: false);
}
