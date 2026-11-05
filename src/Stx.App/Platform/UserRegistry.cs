using System.Runtime.InteropServices;
using Microsoft.Win32;
using Stx.Core.Library;

namespace Stx.App.Platform;

/// <summary>The current user's own registry hive. STX never writes outside it, so it never needs an administrator.</summary>
public sealed class UserRegistry : IUserRegistry
{
    public string? Read(string key, string? name)
    {
        using var opened = Registry.CurrentUser.OpenSubKey(key);
        return opened?.GetValue(name) as string;
    }

    public void Write(string key, string? name, string value)
    {
        using var created = Registry.CurrentUser.CreateSubKey(key);
        created.SetValue(name, value, RegistryValueKind.String);
    }

    /// <summary>Tells Explorer the file associations changed, so "Open with" and Default Apps see STX at once.</summary>
    public static void AssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    const int SHCNE_ASSOCCHANGED = 0x08000000;
    const uint SHCNF_IDLIST = 0;

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
