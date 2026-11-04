using System.Text.RegularExpressions;
using Stx.Core.Settings;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>The guide tells people which pads to press, so every pad it names has to be on the console.</summary>
public class GuideTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Panels() => string.Concat(Directory.GetFiles(Path.Combine(Root(), "src", "Stx.App", "Panels"), "*.cs").Where(f => !f.EndsWith("GuidePanel.cs", StringComparison.Ordinal)).Select(File.ReadAllText));

    static IEnumerable<GuideSection> Sections() => GuideContent.Columns.SelectMany(column => column);

    [Fact]
    public void Every_control_the_guide_names_is_on_the_console()
    {
        string panels = Panels();
        foreach (string control in Sections().SelectMany(section => section.Controls).Distinct())
            Assert.True(panels.Contains($"\"{control}\"", StringComparison.Ordinal), $"The guide names {control}, and no panel has it.");
    }

    [Fact]
    public void Every_control_a_section_lists_is_in_that_sections_words()
    {
        foreach (var section in Sections())
        {
            string words = string.Join(' ', section.Lines);
            foreach (string control in section.Controls)
                Assert.True(words.Contains(control, StringComparison.Ordinal), $"{section.Title} lists {control} and doesn't mention it.");
        }
    }

    [Fact]
    public void The_guide_covers_each_plate_on_the_console()
    {
        var titles = Sections().Select(section => section.Title).ToList();
        foreach (string plate in new[] { "Queue", "Library", "Source truth", "Settings" }) Assert.Contains(plate, titles);
    }

    [Fact]
    public void The_guide_and_tutorial_pads_open_their_plates_from_the_real_window()
    {
        string window = File.ReadAllText(Path.Combine(Root(), "src", "Stx.App", "MainWindow.cs"));
        Assert.Contains("rack.Children.Add(_guide)", window);
        Assert.Contains("rack.Children.Add(_tutorial)", window);
        Assert.Contains("ShowHelp(_guide.Visibility", window);
        Assert.Contains("ShowHelp(_tutorial.Visibility", window);
    }

    [Fact]
    public void The_real_window_opens_the_guide_on_first_launch() =>
        Assert.Contains("if (_app.Settings.TakeFirstLaunchGuide()) ShowHelp(_guide);", File.ReadAllText(Path.Combine(Root(), "src", "Stx.App", "MainWindow.cs")));

    [Fact]
    public void The_guide_opens_by_itself_on_the_first_launch_and_never_again()
    {
        string file = Path.Combine(Path.GetTempPath(), "stx-guide-" + Guid.NewGuid() + ".json");
        try
        {
            var first = new SettingsViewModel(new SettingsStore(file), new FakeStartup());
            Assert.True(first.TakeFirstLaunchGuide());
            Assert.False(first.TakeFirstLaunchGuide());

            // The next launch reads it back from the file.
            Assert.False(new SettingsViewModel(new SettingsStore(file), new FakeStartup()).TakeFirstLaunchGuide());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Someone_who_already_had_settings_still_sees_the_guide_once()
    {
        string file = Path.Combine(Path.GetTempPath(), "stx-guide-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, "{\"MaxParallel\":5}");
            var settings = new SettingsViewModel(new SettingsStore(file), new FakeStartup());
            Assert.True(settings.TakeFirstLaunchGuide());
            Assert.Equal(5, new SettingsStore(file).Load().MaxParallel);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // A knob's scale says where it's set, not what it sets, so a knob without its caption can't be identified.
    [Fact]
    public void Every_knob_on_a_plate_shows_its_caption()
    {
        string panels = Panels();
        int knobs = Regex.Count(panels, @"\bnew Rotary\b|\bRotary _\w+ = new\(\)");
        Assert.True(knobs >= 3, $"Found {knobs} knobs, and the console has at least three.");
        Assert.Equal(knobs, Regex.Count(panels, @"\.Captioned\(\)"));
    }

    [Fact]
    public void The_tutorial_ships_beside_the_app_and_a_release_cannot_leave_it_out()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "src", "Stx.App", "Assets", "tutorial.mp4")), "The video isn't in Assets.");
        Assert.Contains(@"Assets\tutorial.mp4", File.ReadAllText(Path.Combine(Root(), "src", "Stx.App", "Stx.App.csproj")));
        Assert.Contains(@"Assets\tutorial.mp4", File.ReadAllText(Path.Combine(Root(), "tools", "Publish.ps1")));
    }
}
