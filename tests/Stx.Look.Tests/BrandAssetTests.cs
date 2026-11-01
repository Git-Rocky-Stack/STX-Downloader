using System.Security.Cryptography;
using Xunit;

namespace Stx.Look.Tests;

public class BrandAssetTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string InApp(params string[] parts) => Path.Combine([Root(), "src", "Stx.App", .. parts]);

    [Fact]
    public void The_top_rack_uses_the_badge_rocky_chose() =>
        Assert.Equal(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(Root(), "icons", "badge.png"))),
            SHA256.HashData(File.ReadAllBytes(InApp("Assets", "badge.png"))));

    [Fact]
    public void The_top_rack_no_longer_draws_the_name_as_text()
    {
        string header = File.ReadAllText(InApp("Panels", "HeaderPanel.cs"));
        Assert.DoesNotContain("Text = \"STX\"", header);
        Assert.Contains("badge.png", header);
    }

    [Fact]
    public void The_badge_has_a_name_for_screen_readers() =>
        Assert.Contains("SetName", File.ReadAllText(InApp("Panels", "HeaderPanel.cs")));

    [Fact]
    public void The_icon_file_holds_every_size_windows_asks_for()
    {
        using var reader = new BinaryReader(File.OpenRead(InApp("Assets", "stx.ico")));
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int count = reader.ReadUInt16();
        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            reader.BaseStream.Seek(15, SeekOrigin.Current);
        }
        foreach (int needed in new[] { 16, 24, 32, 48, 256 }) Assert.Contains(needed, sizes);
    }

    [Fact]
    public void The_exe_carries_the_icon_and_the_assets_ship_beside_it()
    {
        string project = File.ReadAllText(InApp("Stx.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\stx.ico</ApplicationIcon>", project);
        Assert.Contains(@"Assets\badge.png", project);
        Assert.Contains("CopyToOutputDirectory", project);
    }

    [Fact]
    public void The_window_title_bar_shows_the_icon() =>
        Assert.Contains("SetIcon", File.ReadAllText(InApp("MainWindow.cs")));

    // A focus ring appears when the last input anywhere was the keyboard, so an activated window's capture
    // depended on whether someone happened to be typing. Unactivated, it can't, and it takes no focus from anyone.
    [Fact]
    public void Held_captures_never_activate_their_window()
    {
        string app = File.ReadAllText(InApp("App.xaml.cs"));
        Assert.Contains("activateWindow: false", app);
    }
}
