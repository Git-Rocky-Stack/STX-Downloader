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

    // Phase 4 (Rocky): VLC's DVD plugins ship for unencrypted DVDs. They have libdvdcss built in, which decrypts CSS,
    // so STX checks every disc itself first (DiscCheck) and refuses an encrypted one. Nothing else may carry libdvdcss.
    [Fact]
    public void The_dvd_plugins_ship_and_are_the_only_ones_with_libdvdcss()
    {
        string[] dvd = ["libdvdnav_plugin.dll", "libdvdread_plugin.dll"];
        foreach (var plugin in dvd) Assert.True(File.Exists(Engine("win-x64", "plugins", "access", plugin)), plugin + " is missing");
        var withCss = Directory.EnumerateFiles(Engine("win-x64", "plugins"), "*.dll", SearchOption.AllDirectories)
            .Where(f => File.ReadAllBytes(f).AsSpan().IndexOf("libdvdcss"u8) >= 0).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(dvd, withCss);
    }

    // Blu-ray plays only unencrypted discs: no AACS or BD+ library ships, and libbluray finds none to load.
    [Fact]
    public void Blu_ray_ships_without_any_aacs_library()
    {
        Assert.True(File.Exists(Engine("win-x64", "plugins", "access", "liblibbluray_plugin.dll")) || Directory.EnumerateFiles(Engine("win-x64", "plugins", "access"), "*bluray*").Any(), "no Blu-ray plugin");
        Assert.Empty(Directory.EnumerateFiles(Engine("win-x64"), "*aacs*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(Engine("win-x64"), "*bdplus*", SearchOption.AllDirectories));
    }

    // FluidR3_GM ships as SoundFont 3 (2026-11-07): its samples in Ogg Vorbis, 148 MB down to 27 MB.
    [Fact]
    public void The_soundfont_ships_with_its_license()
    {
        long size = new FileInfo(Engine("soundfonts", "FluidR3_GM.sf3")).Length;
        Assert.InRange(size, 20_000_000, 40_000_000);
        // The engine as fetched (.engine/<version>), not this folder, which can keep an older build's SF2.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        string version = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tools", "vlc", "engine.json"))).RootElement.GetProperty("version").GetString()!;
        string fetched = Path.Combine(dir.FullName, ".engine", version, "libvlc", "soundfonts");
        Assert.True(File.Exists(Path.Combine(fetched, "FluidR3_GM.sf3")), fetched);
        Assert.Empty(Directory.EnumerateFiles(fetched, "*.sf2"));
        Assert.Contains("MIT license", File.ReadAllText(Engine("soundfonts", "FluidR3_GM-COPYRIGHT.txt")));
    }
}
