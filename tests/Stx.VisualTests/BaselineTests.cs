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
        foreach (string gallery in new[] { "faceplate", "display", "led", "bargraph", "sevenseg", "pad", "pushswitch", "rotary", "label", "list", "textbox", "dialog", "states", "all-primitives", "console", "sourcetruth", "library", "settings", "firstrun", "player", "player-audio" })
            foreach (string mode in new[] { "dark", "light" })
                Assert.True(File.Exists(Path.Combine(baselines, $"{gallery}-{mode}.png")), $"No approved image for {gallery} {mode}");
    }

    /// <summary>
    /// Exactly the app's content area. Anything larger would carry pixels from the desktop into a public repo. The
    /// gallery window is 960x640 (944x601 inside), and 960x800 (944x761) for the settings and Library plates, which grew
    /// in Phases 2 to 7 (src/Stx.App/Gallery/GalleryWindow.cs). This test still asked 944x601 of those two after their
    /// taller baselines were approved on 2026-11-05, so it failed there until 2026-11-07.
    /// </summary>
    [Fact]
    public void Baselines_hold_only_the_app_content()
    {
        string baselines = Path.Combine(Root(), "tests", "baselines");
        foreach (string file in Directory.GetFiles(baselines, "*.png", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            int height = name.StartsWith("settings-", StringComparison.Ordinal) || name.StartsWith("library-", StringComparison.Ordinal) ? 761 : 601;
            using var image = new System.Drawing.Bitmap(file);
            Assert.True(image.Width == 944 && image.Height == height, $"{name} is {image.Width}x{image.Height}, not 944x{height}");
        }
    }

    [Theory]
    [MemberData(nameof(Approved))]
    public void Gallery_still_matches_its_approved_image(string gallery, string mode)
    {
        string root = Root();
        string fresh = Path.Combine(root, "tests", "Stx.VisualTests", "output", $"{gallery}-{mode}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);

        // The player's galleries start VLC's engine and hold on the first picture, which takes longer than a still plate.
        int settle = gallery.StartsWith("player", StringComparison.Ordinal) ? 6 : 3;
        var capture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh",
            $"-NoProfile -File \"{Path.Combine(root, "tools", "Capture-Gallery.ps1")}\" -Name {gallery} -Mode {mode} -Out \"{fresh}\" -SettleSeconds {settle}")
        { UseShellExecute = false, RedirectStandardOutput = true })!;
        capture.StandardOutput.ReadToEnd();
        capture.WaitForExit();
        Assert.Equal(0, capture.ExitCode);

        var result = ScreenshotDiff.Compare(fresh, Path.Combine(root, "tests", "baselines", $"{gallery}-{mode}.png"));
        Assert.True(result.Pass, $"{gallery} {mode}: {result.ChangedRatio:P2} of pixels changed");
    }

    // Seen 2026-11-04: PrintWindow answers pure black for a window that hasn't presented a frame, and the content a few
    // hundred milliseconds later. That happened after the 3 s settle too: 3 of 15 console captures, and 3 baseline failures.
    // Capturing the moment the window appears makes it happen every time, so that is what this test does.
    [Fact]
    public void A_capture_is_never_a_window_that_has_not_drawn_yet()
    {
        string root = Root();
        string dir = Directory.CreateTempSubdirectory("stx-capture").FullName;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                string file = Path.Combine(dir, $"console-{i}.png");
                var capture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh",
                    $"-NoProfile -File \"{Path.Combine(root, "tools", "Capture-Gallery.ps1")}\" -Name console -Mode dark -Out \"{file}\" -SettleSeconds 0")
                { UseShellExecute = false, RedirectStandardOutput = true })!;
                capture.StandardOutput.ReadToEnd();
                capture.WaitForExit();
                Assert.Equal(0, capture.ExitCode);
                Assert.False(ScreenshotDiff.IsBlank(file), $"Capture {i + 1} of 3 is pure black");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
