using System.Diagnostics;
using System.Windows.Automation;
using Xunit;

namespace Stx.VisualTests;

/// <summary>
/// Launches each gallery of the Release build and reads it through UI Automation, as a screen reader would.
/// Build first: dotnet build src/Stx.App -c Release -p:Platform=x64
/// </summary>
public class AccessibilityScanTests
{
    /// <summary>Every gallery with the fewest interactive controls it should expose, so an empty page cannot pass.</summary>
    static readonly (string Name, int Interactive)[] Galleries =
    [
        ("faceplate", 0), ("display", 0), ("led", 0), ("bargraph", 0), ("sevenseg", 0),
        ("pad", 7), ("pushswitch", 5), ("rotary", 3),
        ("label", 0), ("list", 5), ("textbox", 2), ("dialog", 2), ("states", 1),
        ("all-primitives", 9),
        ("console", 11), ("sourcetruth", 2), ("library", 8), ("settings", 9), ("firstrun", 1),
    ];

    public static IEnumerable<object[]> Cases() =>
        from gallery in Galleries
        from mode in new[] { "dark", "light" }
        select new object[] { gallery.Name, mode, gallery.Interactive };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_interactive_element_has_a_name_and_a_44_px_target(string gallery, string mode, int fewest)
    {
        using var app = Launch(gallery, mode);
        try
        {
            var found = ScanWhenReady(app, fewest);
            var problems = new List<string>();
            foreach (var (type, name, width, height) in found)
            {
                string who = $"{type} '{name}' {width:F0}x{height:F0}";
                if (string.IsNullOrWhiteSpace(name)) problems.Add($"no name: {who}");
                if (width < 44 || height < 44) problems.Add($"too small: {who}");
            }
            Assert.True(found.Count >= fewest, $"{gallery} {mode}: found {found.Count} interactive elements, expected at least {fewest}");
            Assert.True(problems.Count == 0, $"{gallery} {mode}:\n" + string.Join("\n", problems));
        }
        finally
        {
            if (!app.HasExited) app.Kill();
        }
    }

    static Process Launch(string gallery, string mode)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        string exe = Path.Combine(dir!.FullName, "src", "Stx.App", "bin", "x64", "Release", "net10.0-windows10.0.22621.0", "win-x64", "STXDownloader.exe");
        Assert.True(File.Exists(exe), $"Build the Release app first: {exe}");
        return Process.Start(new ProcessStartInfo(exe, $"--gallery={gallery} --mode={mode}") { UseShellExecute = false })!;
    }

    static List<(string Type, string Name, double Width, double Height)> ScanWhenReady(Process app, int fewest)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (app.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
            app.Refresh();
        }
        Assert.NotEqual(IntPtr.Zero, app.MainWindowHandle);

        // Give the page time to lay out, and keep trying while slower pages add their controls.
        Thread.Sleep(1500);
        var found = Scan(app.MainWindowHandle);
        while (found.Count < fewest && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            found = Scan(app.MainWindowHandle);
        }
        return found;
    }

    static List<(string Type, string Name, double Width, double Height)> Scan(IntPtr window)
    {
        var found = new List<(string, string, double, double)>();
        Walk(AutomationElement.FromHandle(window), found);
        return found;
    }

    static void Walk(AutomationElement element, List<(string, string, double, double)> found)
    {
        var child = TreeWalker.RawViewWalker.GetFirstChild(element);
        while (child is not null)
        {
            // A scrollbar's small arrows are exempt: the list scrolls by wheel, touch and keyboard as well.
            if (child.Current.ControlType != ControlType.ScrollBar)
            {
                if (IsInteractive(child))
                {
                    var box = child.Current.BoundingRectangle;
                    found.Add((child.Current.ControlType.ProgrammaticName, child.Current.Name, box.Width, box.Height));
                }
                Walk(child, found);
            }
            child = TreeWalker.RawViewWalker.GetNextSibling(child);
        }
    }

    static bool IsInteractive(AutomationElement e)
    {
        // Only the app's own controls. The window's title bar buttons belong to Windows.
        if (e.Current.FrameworkId != "XAML" || e.Current.IsOffscreen) return false;
        return e.Current.IsKeyboardFocusable
            || Has(e, AutomationElement.IsInvokePatternAvailableProperty)
            || Has(e, AutomationElement.IsTogglePatternAvailableProperty)
            || Has(e, AutomationElement.IsSelectionItemPatternAvailableProperty)
            || Has(e, AutomationElement.IsValuePatternAvailableProperty);
    }

    static bool Has(AutomationElement e, AutomationProperty property) => e.GetCurrentPropertyValue(property) is true;
}
