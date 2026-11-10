using System.Text.Json;
using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// .NET ships inside the app, so the setup runs on a PC that has never had .NET, and asks for no administrator.
/// 1.1.0's first setup bundled the Windows App SDK but not .NET, and stopped on a clean PC with "You must install .NET"
/// (2026-11-10). A runtimeconfig that names a framework is one Windows has to find installed.
/// </summary>
public class RuntimeShipsTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Built(params string[] parts) =>
        Path.Combine([Root(), "src", "Stx.App", "bin", "x64", "Release", "net10.0-windows10.0.22621.0", "win-x64", .. parts]);

    [Fact]
    public void The_app_is_built_with_dotnet_inside_it()
    {
        string project = File.ReadAllText(Path.Combine(Root(), "src", "Stx.App", "Stx.App.csproj"));
        Assert.Contains("<SelfContained>true</SelfContained>", project);
        Assert.Contains("<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>", project);
    }

    [Fact]
    public void A_release_cannot_leave_dotnet_out()
    {
        string publish = File.ReadAllText(Path.Combine(Root(), "tools", "Publish.ps1"));
        foreach (string needed in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll" })
            Assert.Contains($"'{needed}'", publish);
        Assert.Contains("includedFrameworks", publish);
    }

    [Fact]
    public void The_built_app_carries_dotnet_beside_it()
    {
        Assert.True(File.Exists(Built("STXDownloader.exe")), "Build the Release app first.");
        foreach (string file in new[] { "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "System.Private.CoreLib.dll" })
            Assert.True(File.Exists(Built(file)), $"{file} isn't beside the app, so it would need .NET installed.");

        using var config = JsonDocument.Parse(File.ReadAllText(Built("STXDownloader.runtimeconfig.json")));
        JsonElement options = config.RootElement.GetProperty("runtimeOptions");
        Assert.False(options.TryGetProperty("framework", out _), "The app asks Windows for an installed .NET.");
        Assert.False(options.TryGetProperty("frameworks", out _), "The app asks Windows for an installed .NET.");
        Assert.True(options.TryGetProperty("includedFrameworks", out JsonElement included) && included.GetArrayLength() > 0, "The app names no .NET of its own.");
    }
}
