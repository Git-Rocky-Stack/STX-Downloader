using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// One release number: Directory.Build.props sets it for every built file, tools/Publish.ps1 reads it for the setup's
/// name, and the browser extension, which ships inside the same release, carries it too.
/// </summary>
public class VersionTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Release() =>
        XDocument.Load(Path.Combine(Root(), "Directory.Build.props")).Descendants("Version").Single().Value;

    [Fact]
    public void The_release_number_is_a_plain_three_part_version()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", Release());
    }

    [Fact]
    public void The_extension_carries_the_apps_release_number()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "extension", "manifest.json")));
        Assert.Equal(Release(), manifest.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void The_built_app_reports_the_release_number()
    {
        var built = typeof(Stx.ViewModels.GuideContent).Assembly.GetName().Version!;
        Assert.Equal(Release(), $"{built.Major}.{built.Minor}.{built.Build}");
    }

    // Publish.ps1 and the installer each had their own "1.0.0" until 1.1.0, so a bump could miss one.
    [Fact]
    public void Nothing_but_the_props_file_names_a_default_release_number()
    {
        Assert.DoesNotMatch(@"\$Version\s*=\s*'\d", File.ReadAllText(Path.Combine(Root(), "tools", "Publish.ps1")));
        Assert.DoesNotMatch(@"#define AppVersion", File.ReadAllText(Path.Combine(Root(), "tools", "installer", "STXDownloader.iss")));
    }
}
