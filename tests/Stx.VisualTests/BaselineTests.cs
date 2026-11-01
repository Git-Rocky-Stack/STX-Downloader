using Xunit;

namespace Stx.VisualTests;

/// <summary>Holds the look still: a fresh capture of each gallery must match the image Rocky approved.</summary>
public class BaselineTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    public static IEnumerable<object[]> Approved()
    {
        string baselines = Path.Combine(Root(), "tests", "baselines");
        foreach (string file in Directory.GetFiles(baselines, "*.png"))
        {
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('-');
            yield return new object[] { string.Join('-', parts[..^1]), parts[^1] };
        }
    }

    [Fact]
    public void Every_primitive_has_an_approved_image_in_both_modes()
    {
        string baselines = Path.Combine(Root(), "tests", "baselines");
        foreach (string gallery in new[] { "faceplate", "display", "led", "bargraph", "sevenseg", "pad", "pushswitch", "rotary", "label", "list", "textbox", "dialog", "states", "all-primitives" })
            foreach (string mode in new[] { "dark", "light" })
                Assert.True(File.Exists(Path.Combine(baselines, $"{gallery}-{mode}.png")), $"No approved image for {gallery} {mode}");
    }

    /// <summary>Exactly the app's content area. Anything larger would carry pixels from the desktop into a public repo.</summary>
    [Fact]
    public void Baselines_hold_only_the_app_content()
    {
        string baselines = Path.Combine(Root(), "tests", "baselines");
        foreach (string file in Directory.GetFiles(baselines, "*.png", SearchOption.AllDirectories))
        {
            using var image = new System.Drawing.Bitmap(file);
            Assert.True(image.Width == 944 && image.Height == 601, $"{Path.GetFileName(file)} is {image.Width}x{image.Height}");
        }
    }

    [Theory]
    [MemberData(nameof(Approved))]
    public void Gallery_still_matches_its_approved_image(string gallery, string mode)
    {
        string root = Root();
        string fresh = Path.Combine(root, "tests", "Stx.VisualTests", "output", $"{gallery}-{mode}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);

        var capture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh",
            $"-NoProfile -File \"{Path.Combine(root, "tools", "Capture-Gallery.ps1")}\" -Name {gallery} -Mode {mode} -Out \"{fresh}\"")
        { UseShellExecute = false, RedirectStandardOutput = true })!;
        capture.StandardOutput.ReadToEnd();
        capture.WaitForExit();
        Assert.Equal(0, capture.ExitCode);

        var result = ScreenshotDiff.Compare(fresh, Path.Combine(root, "tests", "baselines", $"{gallery}-{mode}.png"));
        Assert.True(result.Pass, $"{gallery} {mode}: {result.ChangedRatio:P2} of pixels changed");
    }
}
