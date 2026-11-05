using Xunit;

namespace Stx.App.Tests;

/// <summary>The VLC engine and the SoundFont ship beside the app, and a release can't leave them out.</summary>
public class PlaybackShipsTests
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
    public void A_release_cannot_leave_the_engine_or_the_soundfont_out()
    {
        string publish = File.ReadAllText(Path.Combine(Root(), "tools", "Publish.ps1"));
        foreach (string needed in new[] { @"libvlc\win-x64\libvlc.dll", @"libvlc\win-x64\libvlccore.dll", @"libvlc\win-x64\plugins\plugins.dat", @"libvlc\soundfonts\FluidR3_GM.sf2", @"libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt" })
            Assert.Contains($"'{needed}'", publish);
    }

    [Fact]
    public void The_built_app_carries_the_engine_beside_it()
    {
        Assert.True(File.Exists(Built("STXDownloader.exe")), "Build the Release app first.");
        Assert.True(File.Exists(Built("libvlc", "win-x64", "libvlc.dll")));
        Assert.True(Directory.EnumerateFiles(Built("libvlc", "win-x64", "plugins"), "*.dll", SearchOption.AllDirectories).Count() > 250);
        Assert.True(File.Exists(Built("libvlc", "soundfonts", "FluidR3_GM.sf2")));
    }

    // Spike S1, finding F3: only the x64 engine ships, and nothing that runs site scripts or decrypts DVDs.
    [Fact]
    public void Only_x64_libvlc_ships_and_no_lua()
    {
        Assert.False(Directory.Exists(Built("libvlc", "win-x86")), "an x86 engine ships");
        Assert.False(Directory.Exists(Built("libvlc", "win-arm64")), "an ARM64 engine ships");
        Assert.False(Directory.Exists(Built("libvlc", "win-x64", "plugins", "lua")), "the Lua plugin ships");
        Assert.False(File.Exists(Built("libvlc", "win-x64", "plugins", "access", "libdvdcss_plugin.dll")) || File.Exists(Built("libvlc", "win-x64", "plugins", "access", "libdvdnav_plugin.dll")), "a DVD plugin with libdvdcss ships");
    }
}
