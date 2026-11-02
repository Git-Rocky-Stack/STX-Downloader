using Microsoft.Win32;
using Stx.ViewModels;

namespace Stx.App.Platform;

/// <summary>Starting with Windows, through your own user's Run key. Nothing outside your account is touched.</summary>
public sealed class StartupRegistry : IStartupRegistration
{
    public const string HiddenFlag = "--hidden";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "STXDownloader";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {HiddenFlag}");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
