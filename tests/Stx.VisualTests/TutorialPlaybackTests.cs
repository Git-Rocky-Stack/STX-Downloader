using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Xunit;

namespace Stx.VisualTests;

/// <summary>
/// Opens the real Tutorial plate and watches it. Playing is proven by the picture changing over time, not by
/// reading a flag, because the bug this guards against left every flag looking right while the video sat still.
/// </summary>
public class TutorialPlaybackTests
{
    // Inside the player and above its transport bar, in the 944x601 capture.
    static readonly Rectangle Picture = new(60, 120, 820, 330);

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Capture(string label, int settleSeconds, bool reduceMotion)
    {
        string root = Root();
        string file = Path.Combine(root, "tests", "Stx.VisualTests", "output", $"tutorial-{label}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string flag = reduceMotion ? " -ReduceMotion" : "";
        var capture = Process.Start(new ProcessStartInfo("pwsh",
            $"-NoProfile -File \"{Path.Combine(root, "tools", "Capture-Gallery.ps1")}\" -Name tutorial -Mode dark -Out \"{file}\" -SettleSeconds {settleSeconds}{flag}")
        { UseShellExecute = false, RedirectStandardOutput = true })!;
        capture.StandardOutput.ReadToEnd();
        capture.WaitForExit();
        Assert.Equal(0, capture.ExitCode);
        return file;
    }

    /// <summary>Share of the picture area that differs between two captures.</summary>
    static double Changed(string a, string b)
    {
        using var x = new Bitmap(a);
        using var y = new Bitmap(b);
        int changed = 0, total = 0;
        for (int row = Picture.Top; row < Picture.Bottom; row += 2)
        for (int col = Picture.Left; col < Picture.Right; col += 2)
        {
            Color p = x.GetPixel(col, row), q = y.GetPixel(col, row);
            if (Math.Abs(p.R - q.R) > 12 || Math.Abs(p.G - q.G) > 12 || Math.Abs(p.B - q.B) > 12) changed++;
            total++;
        }
        return changed / (double)total;
    }

    /// <summary>Share of the picture area that is lit, rather than the black the video opens on.</summary>
    static double Lit(string file)
    {
        using var image = new Bitmap(file);
        int lit = 0, total = 0;
        for (int row = Picture.Top; row < Picture.Bottom; row += 2)
        for (int col = Picture.Left; col < Picture.Right; col += 2)
        {
            Color p = image.GetPixel(col, row);
            if (p.R + p.G + p.B > 3 * 60) lit++;
            total++;
        }
        return lit / (double)total;
    }

    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, ref bool value, uint ini);

    /// <summary>Windows' "Animation effects" switch. With it off, the app treats reduce motion as on.</summary>
    static bool WindowsAnimates()
    {
        bool on = true;
        SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref on, 0);
        return on;
    }

    [Fact]
    public void The_tutorial_plays_by_itself_when_motion_is_allowed()
    {
        string early = Capture("early", 4, reduceMotion: false);
        string later = Capture("later", 11, reduceMotion: false);
        double changed = Changed(early, later);

        if (WindowsAnimates())
            Assert.True(changed > 0.05, $"The picture barely changed ({changed:P1}) between 4 s and 11 s, so the video didn't play.");
        else
            Assert.True(changed < 0.01, $"Windows animation effects are off, yet the picture changed ({changed:P1}), so the video played by itself.");
    }

    [Fact]
    public void With_reduce_motion_the_tutorial_waits_on_its_poster()
    {
        string early = Capture("still-early", 4, reduceMotion: true);
        string later = Capture("still-later", 11, reduceMotion: true);

        double changed = Changed(early, later);
        Assert.True(changed < 0.01, $"The picture changed ({changed:P1}) with reduce motion on, so the video started by itself.");
        double lit = Lit(later);
        Assert.True(lit > 0.02, $"Only {lit:P1} of the picture is lit, so it shows black instead of the poster.");
    }

    [Fact]
    public void With_reduce_motion_pressing_play_starts_the_video()
    {
        string exe = Path.Combine(Root(), "src", "Stx.App", "bin", "x64", "Release", "net10.0-windows10.0.22621.0", "win-x64", "STXDownloader.exe");
        Assert.True(File.Exists(exe), $"Build the Release app first: {exe}");
        using var app = Process.Start(new ProcessStartInfo(exe, "--gallery=tutorial --mode=dark --reduce-motion") { UseShellExecute = false })!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (app.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline) { Thread.Sleep(100); app.Refresh(); }
            Assert.NotEqual(IntPtr.Zero, app.MainWindowHandle);
            var window = System.Windows.Automation.AutomationElement.FromHandle(app.MainWindowHandle);

            var play = Find(window, deadline, e => e.Current.Name == "PLAY" && e.GetCurrentPropertyValue(System.Windows.Automation.AutomationElement.IsInvokePatternAvailableProperty) is true);
            Assert.NotNull(play);
            Assert.Null(Seek(window));   // Nothing is loaded before PLAY.

            ((System.Windows.Automation.InvokePattern)play!.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern)).Invoke();
            Thread.Sleep(6000);

            var seek = Seek(window);
            Assert.NotNull(seek);
            double at = ((System.Windows.Automation.RangeValuePattern)seek!.GetCurrentPattern(System.Windows.Automation.RangeValuePattern.Pattern)).Current.Value;
            Assert.True(at > 0, $"Six seconds after PLAY the video is still at {at}.");
        }
        finally
        {
            if (!app.HasExited) app.Kill();
        }
    }

    static System.Windows.Automation.AutomationElement? Find(System.Windows.Automation.AutomationElement root, DateTime deadline, Func<System.Windows.Automation.AutomationElement, bool> match)
    {
        do
        {
            foreach (System.Windows.Automation.AutomationElement e in root.FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
                if (match(e)) return e;
            Thread.Sleep(300);
        } while (DateTime.UtcNow < deadline);
        return null;
    }

    /// <summary>The player's seek bar. It only exists once the video is loaded.</summary>
    static System.Windows.Automation.AutomationElement? Seek(System.Windows.Automation.AutomationElement root) =>
        Find(root, DateTime.UtcNow.AddSeconds(2), e => e.Current.ControlType == System.Windows.Automation.ControlType.Slider
            && e.GetCurrentPropertyValue(System.Windows.Automation.AutomationElement.IsRangeValuePatternAvailableProperty) is true);

    [Fact]
    public void The_poster_ships_beside_the_video()
    {
        string root = Root();
        Assert.True(File.Exists(Path.Combine(root, "src", "Stx.App", "Assets", "tutorial-poster.png")), "The poster isn't in Assets.");
        Assert.Contains(@"Assets\tutorial-poster.png", File.ReadAllText(Path.Combine(root, "src", "Stx.App", "Stx.App.csproj")));
        Assert.Contains(@"Assets\tutorial-poster.png", File.ReadAllText(Path.Combine(root, "tools", "Publish.ps1")));
    }
}
