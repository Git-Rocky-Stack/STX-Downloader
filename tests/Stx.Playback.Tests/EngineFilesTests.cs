namespace Stx.Playback.Tests;

/// <summary>The engine beside the tests is the one STX ships: the official VLC build, pruned, with the SoundFont.</summary>
public class EngineFilesTests
{
    static string Engine(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "libvlc", .. parts]);

    [Fact]
    public void It_is_the_official_build_with_the_plugins_nuget_lacked()
    {
        Assert.True(File.Exists(Engine("win-x64", "libvlc.dll")));
        // These are in VLC 3.0.24 for Windows and missing from the VideoLAN.LibVLC.Windows package.
        foreach (var plugin in new[] { "packetizer/libpacketizer_a52_plugin.dll", "demux/libreal_plugin.dll", "demux/libmpc_plugin.dll", "codec/libfaad_plugin.dll" })
            Assert.True(File.Exists(Engine("win-x64", "plugins", plugin)), plugin + " is missing");
        Assert.True(File.Exists(Engine("win-x64", "plugins", "plugins.dat")), "the plugin cache wasn't rebuilt");
    }

    [Fact]
    public void Nothing_that_opens_ports_or_runs_scripts_ships()
    {
        foreach (var folder in new[] { "lua", "gui", "control", "services_discovery", "stream_out", "access_output" })
            Assert.False(Directory.Exists(Engine("win-x64", "plugins", folder)), folder + " ships");
        Assert.False(Directory.Exists(Engine("win-x86")) || Directory.Exists(Engine("win-arm64")), "a non-x64 engine ships");
    }

    // VLC's DVD plugins have libdvdcss built in, which decrypts CSS-protected DVDs. STX doesn't do DRM.
    [Fact]
    public void Nothing_that_decrypts_dvds_ships()
    {
        foreach (var plugin in new[] { "access/libdvdnav_plugin.dll", "access/libdvdread_plugin.dll" })
            Assert.False(File.Exists(Engine("win-x64", "plugins", plugin)), plugin + " ships");
        var withCss = Directory.EnumerateFiles(Engine("win-x64", "plugins"), "*.dll", SearchOption.AllDirectories)
            .Where(f => File.ReadAllBytes(f).AsSpan().IndexOf("libdvdcss"u8) >= 0).Select(Path.GetFileName).ToList();
        Assert.True(withCss.Count == 0, "Plugins with libdvdcss inside: " + string.Join(", ", withCss));
    }

    [Fact]
    public void The_soundfont_ships_with_its_license()
    {
        Assert.True(new FileInfo(Engine("soundfonts", "FluidR3_GM.sf2")).Length > 100_000_000);
        Assert.Contains("MIT license", File.ReadAllText(Engine("soundfonts", "FluidR3_GM-COPYRIGHT.txt")));
    }
}
