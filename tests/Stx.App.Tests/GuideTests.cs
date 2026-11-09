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

    static IEnumerable<GuideSection> Sections() => GuideContent.Sections;

    /// <summary>
    /// What the console has to press or turn, by the name it shows: each pad, switch and help pad, the Setup plate's
    /// action, and each knob's caption (an EngravedLabel, so shown in capitals).
    /// </summary>
    static HashSet<string> ConsoleControls()
    {
        string panels = Panels();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pattern in new[] { @"Content = ""([A-Z][A-Z .\-]*)""", @"Switch\(""([^""]+)""", @"HelpPad\(""([^""]+)""", @"ActionText = ""([^""]+)""" })
            foreach (Match m in Regex.Matches(panels, pattern)) names.Add(m.Groups[1].Value);
        foreach (string pattern in new[] { @"(?:new Rotary|Rotary _\w+ = new\(\))\s*\{\s*Label = ""([^""]+)""",@"Picker\(_\w+, ""([^""]+)""" })
            foreach (Match m in Regex.Matches(panels, pattern)) names.Add(m.Groups[1].Value.ToUpperInvariant());
        // EXIT is FULL's name in full screen, set in code rather than in a literal of its own.
        if (panels.Contains("\"EXIT\"", StringComparison.Ordinal)) names.Add("EXIT");
        return names;
    }

    static IEnumerable<(string Where, IReadOnlyList<string> Lines, IReadOnlyList<string> Controls, IReadOnlyList<string> Readouts)> Blocks() =>
        Sections().Select(s => (s.Title, s.Lines, s.Controls, s.Readouts))
            .Concat(GuideContent.QuickStart.Select(step => (step.Title, (IReadOnlyList<string>)[step.Line], step.Controls, step.Readouts)))
            .Append(("reopen line", [GuideContent.Reopen], GuideContent.ReopenControls, []));

    [Fact]
    public void Every_control_the_guide_names_is_on_the_console()
    {
        var console = ConsoleControls();
        foreach (var block in Blocks())
            foreach (string control in block.Controls)
                Assert.True(console.Contains(control), $"{block.Where} names {control}, and no panel has it.");
    }

    // The other way round: a pad the guide never mentions is one nobody can look up.
    [Fact]
    public void Every_pad_switch_and_knob_on_the_console_is_in_the_guide()
    {
        var named = Blocks().SelectMany(block => block.Controls).ToHashSet(StringComparer.Ordinal);
        // ADD ALL is the playlist pad's placeholder: it always shows ADD and the count, which the guide says.
        foreach (string control in ConsoleControls().Where(c => c != "ADD ALL"))
            Assert.True(named.Contains(control), $"The console has {control}, and the guide never names it.");
    }

    [Fact]
    public void Every_control_and_readout_a_block_lists_is_in_its_words()
    {
        foreach (var block in Blocks())
        {
            string words = string.Join(' ', block.Lines);
            foreach (string name in block.Controls.Concat(block.Readouts))
                Assert.True(GuideContent.Spans(words, [name]).Any(span => span.Marked), $"{block.Where} lists {name} and doesn't say it as a word of its own.");
        }
    }

    // A readout is held to the source: the words as the app writes them, or a label it shows in capitals.
    [Fact]
    public void Every_readout_the_guide_names_is_in_the_source()
    {
        string source = string.Concat(Directory.GetFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !f.EndsWith("GuideContent.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        string shown = source + string.Concat(Regex.Matches(source, @"""([^""\r\n]*)""").Select(m => m.Groups[1].Value.ToUpperInvariant() + "\n"));
        foreach (var block in Blocks())
            foreach (string readout in block.Readouts)
                Assert.True(shown.Contains(readout, StringComparison.Ordinal), $"{block.Where} names {readout}, and the app never shows it.");
    }

    [Fact]
    public void The_guide_covers_each_plate_on_the_console()
    {
        var titles = Sections().Select(section => section.Title).ToList();
        foreach (string plate in new[] { "Queue", "Library", "Source truth", "Settings", "Player" }) Assert.Contains(plate, titles);
    }

    // It opens by itself once, so it has to say where it lives.
    [Fact]
    public void The_guide_says_how_to_open_it_again()
    {
        Assert.Contains("SETTINGS", GuideContent.Reopen);
        Assert.Contains("USER GUIDE", GuideContent.Reopen);
    }

    [Fact]
    public void Every_section_has_a_summary_and_every_step_a_line()
    {
        Assert.All(Sections(), section => Assert.False(string.IsNullOrWhiteSpace(section.Summary), section.Title));
        Assert.InRange(GuideContent.QuickStart.Count, 3, 5);
        Assert.All(GuideContent.QuickStart, step => Assert.False(string.IsNullOrWhiteSpace(step.Line), step.Title));
    }

    [Fact]
    public void Spans_keep_every_letter_in_order()
    {
        foreach (var block in Blocks())
            foreach (string line in block.Lines)
                Assert.Equal(line, string.Concat(GuideContent.Spans(line, [.. block.Controls, .. block.Readouts]).Select(span => span.Text)));
    }

    [Fact]
    public void Spans_mark_whole_names_and_the_longest_name()
    {
        var spans = GuideContent.Spans("OPENED files, then OPEN. PLAY STREAM, not PLAY.", ["OPEN", "PLAY", "PLAY STREAM"]);
        Assert.Equal(["OPEN", "PLAY STREAM", "PLAY"], spans.Where(s => s.Marked).Select(s => s.Text));
        Assert.Equal("OPENED files, then ", spans[0].Text);
        Assert.False(spans[0].Marked);
    }

    // Rocky: Dirac being refused has to be said plainly, in the guide as in the docs.
    [Fact]
    public void The_guide_says_dirac_is_refused_and_why()
    {
        // In the playing sections: the Player's own, or the one beside it for streams and discs, where Dirac streams are.
        string player = string.Join(' ', Sections().Where(s => s.Title.StartsWith("Player", StringComparison.Ordinal) || s.Title.StartsWith("Streams", StringComparison.Ordinal)).SelectMany(s => s.Lines));
        Assert.Contains("Dirac", player);
        Assert.Contains("DIRAC VIDEO CAN'T BE PLAYED SAFELY", player);
        Assert.Contains("crash", player);
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
