using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Stx.Core.Library;
using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// The DEFAULT PLAYER LED reads which app opens each type from the registry itself (FileAssociations.ChosenProgId),
/// because where Windows 11 24H2 keeps the choice isn't documented. This holds that reading to Windows' own answer,
/// AssocQueryString, for every type STX registers, on whatever Windows runs it: read only, nothing is changed. First
/// run on build 26300 (newer than 24H2) on 2026-11-07, where the choices sat in UserChoice and all agreed.
/// </summary>
public class DefaultAppsLiveTests
{
    sealed class RealRegistry : IUserRegistry
    {
        public string? Read(string key, string? name)
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            return opened?.GetValue(name) as string;
        }

        public void Write(string key, string? name, string value) => throw new InvalidOperationException("This test only reads.");
    }

    // ASSOCSTR_PROGID: the ProgId Windows opens the type with, the user's choice included.
    const int AssocProgId = 20;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int AssocQueryString(int flags, int what, string association, string? extra, StringBuilder? result, ref uint length);

    static string? WindowsProgId(string extension)
    {
        var result = new StringBuilder(512);
        uint length = (uint)result.Capacity;
        return AssocQueryString(0, AssocProgId, extension, null, result, ref length) == 0 ? result.ToString() : null;
    }

    [Fact]
    public void Each_types_chosen_app_is_read_as_windows_reads_it()
    {
        var registry = new RealRegistry();
        var compared = new List<string>();
        var differ = new List<string>();
        foreach (string extension in MediaTypes.All)
        {
            // A type the user never chose an app for opens with its class's default, which isn't a choice; STX reads it as none.
            if (FileAssociations.ChosenProgId(registry, extension) is not { } stx) continue;
            compared.Add(extension);
            string? windows = WindowsProgId(extension);
            if (!string.Equals(stx, windows, StringComparison.OrdinalIgnoreCase)) differ.Add($"{extension}: STX read {stx}, Windows says {windows}");
            Assert.Equal(FileAssociations.IsOurs(windows), FileAssociations.IsOurs(stx));
        }
        if (compared.Count == 0) Assert.Skip("Nobody on this PC has chosen an app for any media type, so there's nothing to compare.");
        Assert.True(differ.Count == 0, string.Join(Environment.NewLine, differ));
    }
}
