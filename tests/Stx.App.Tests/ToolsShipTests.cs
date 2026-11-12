using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// yt-dlp, FFmpeg, FFprobe and Deno ship inside the setup, so a first launch has nothing to download (2026-11-11).
/// Until then the app fetched about 430 MB from GitHub before it could save anything, and a GitHub outage or no
/// connection ended a first run with TOOLS MISSING. The setup must take them from where the app's updater does, hold
/// them to the same checksums, put them where the app looks, and never replace a yt-dlp the app has since updated.
/// </summary>
public class ToolsShipTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    static readonly string[] Tools = ["yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe"];

    [Fact]
    public void The_release_takes_the_tools_from_the_app_s_own_sources()
    {
        string publish = Read("tools", "Publish.ps1");
        // Read from the updater's source, never copied into the script, so a moved download moves both.
        Assert.Contains("'ToolVersions.cs' 'YtDlpExe'", publish);
        Assert.Contains("'ToolVersions.cs' 'YtDlpSums'", publish);
        Assert.Contains("'ToolVersions.cs' 'DenoZip'", publish);
        Assert.Contains("'ToolVersions.cs' 'DenoSums'", publish);
        Assert.Contains("'FfmpegSource.cs' 'Release'", publish);
        Assert.Contains("'FfmpegSource.cs' 'ZipName'", publish);
        Assert.DoesNotContain("https://github.com/yt-dlp/yt-dlp/releases", publish);
        Assert.DoesNotContain("https://github.com/denoland/deno/releases", publish);
        Assert.DoesNotContain("https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases", publish);
    }

    [Fact]
    public void The_release_checks_each_tool_before_shipping_it()
    {
        string publish = Read("tools", "Publish.ps1");
        Assert.Contains("Get-FileHash", publish);
        Assert.Contains("but its publisher posts", publish);
        foreach (string tool in Tools)
        {
            Assert.Contains($"'{tool}'", publish);
            Assert.Matches($@"'{System.Text.RegularExpressions.Regex.Escape(tool)}'\s*=\s*Test-Tool", publish);
        }
    }

    [Fact]
    public void The_setup_puts_the_tools_where_the_app_looks_and_keeps_a_newer_one()
    {
        // ToolManager.DefaultRoot: LocalApplicationData\STXDownloader\tools.
        Assert.Contains("\"STXDownloader\", \"tools\"", Read("src", "Stx.Tools", "ToolManager.cs"));

        string setup = Read("tools", "installer", "STXDownloader.iss");
        string line = setup.Split('\n').Single(l => l.StartsWith("Source: \"{#Tools}\\*.exe\"", StringComparison.Ordinal));
        Assert.Contains(@"DestDir: ""{localappdata}\STXDownloader\tools""", line);
        Assert.Contains("onlyifdoesntexist", line);
        // Uninstalling leaves them to the question about the app's data, as it did when the app downloaded them.
        Assert.Contains("uninsneveruninstall", line);
        Assert.Contains("/DTools=", Read("tools", "Publish.ps1"));
    }

    [Fact]
    public void The_tools_licenses_ship_with_them()
    {
        foreach (string license in new[] { "yt-dlp-LICENSE.txt", "Deno-LICENSE.txt" })
            Assert.True(File.Exists(Path.Combine(Root(), "tools", "installer", "licenses", license)), $"{license} is missing.");
        string publish = Read("tools", "Publish.ps1");
        // FFmpeg's GPL text comes out of the very archive the programs do, and the notice says where its source is.
        Assert.Contains("'LICENSE.txt' = (Join-Path $licenses 'FFmpeg-LICENSE.txt')", publish);
        Assert.Contains("https://github.com/FFmpeg/FFmpeg", publish);
        Assert.Contains(@"Source: ""{#Tools}\licenses\*""; DestDir: ""{app}\docs\licenses""", Read("tools", "installer", "STXDownloader.iss"));
    }
}
