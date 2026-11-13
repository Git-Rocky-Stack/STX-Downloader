using Stx.Core.Library;
using Stx.Core.Settings;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

sealed class RegistrySpy : IUserRegistry
{
    public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>When true, every write is refused, as Group Policy refuses one to a locked Software\Classes.</summary>
    public bool Locked;
    public string? Read(string key, string? name) => Values.GetValueOrDefault(key + "|" + name);
    public void Write(string key, string? name, string value)
    {
        if (Locked) throw new UnauthorizedAccessException($"Access to the registry key '{key}' is denied.");
        Values[key + "|" + name] = value;
    }
    public void Choose(string extension, string progId) => Values[$@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice|ProgId"] = progId;
}

/// <summary>Phase 3: first-run setup asks once, and Settings shows what Windows really does.</summary>
public sealed class DefaultPlayerTests : IDisposable
{
    const string Exe = @"C:\STX\STXDownloader.exe";
    readonly string _file = Path.Combine(Path.GetTempPath(), "stx-default-" + Guid.NewGuid() + ".json");
    readonly RegistrySpy _registry = new();
    readonly List<string> _launched = [];
    int _notified;

    public void Dispose() => File.Delete(_file);

    SettingsViewModel Settings() => new(new SettingsStore(_file), new FakeStartup());

    DefaultPlayerViewModel Make(SettingsViewModel? settings = null, int build = 26300) =>
        new(_registry, _launched.Add, settings ?? Settings(), Exe, build, () => _notified++);

    [Fact]
    public void A_first_run_asks_the_question()
    {
        var player = Make();
        Assert.True(player.ShowOffer);
        Assert.Equal("MAKE STX YOUR PLAYER FOR VIDEOS AND MUSIC?", player.Question);
    }

    [Fact]
    public void Yes_registers_and_opens_default_apps_and_never_asks_again()
    {
        var player = Make();
        player.YesCommand.Execute(null);
        Assert.Equal(["ms-settings:defaultapps?registeredAppUser=STX%20Downloader"], _launched);
        Assert.Equal(1, _notified);
        Assert.NotNull(_registry.Read(FileAssociations.CapabilitiesKey, "ApplicationName"));
        Assert.False(player.ShowOffer);
        Assert.False(Make().ShowOffer);
    }

    // Review 2026-11-07: a policy-locked Software\Classes threw out of the pad's command.
    [Fact]
    public void A_locked_registry_still_opens_default_apps_and_doesnt_throw()
    {
        _registry.Locked = true;
        var player = Make();
        player.ChooseCommand.Execute(null);
        Assert.Equal(["ms-settings:defaultapps?registeredAppUser=STX%20Downloader"], _launched);
        Assert.Equal(0, _notified);
    }

    [Fact]
    public void Not_now_never_asks_again_and_changes_nothing()
    {
        var player = Make();
        player.NotNowCommand.Execute(null);
        Assert.False(player.ShowOffer);
        Assert.False(Make().ShowOffer);
        Assert.Empty(_launched);
        Assert.Empty(_registry.Values);
    }

    // Spec anti-goal: STX setting itself as default without the user's say.
    [Fact]
    public void Nothing_is_made_the_default_by_STX()
    {
        Make().YesCommand.Execute(null);
        Assert.True(FileAssociations.Read(_registry).IsNone);
    }

    [Fact]
    public void Already_the_player_means_no_question()
    {
        foreach (string extension in MediaTypes.All) _registry.Choose(extension, FileAssociations.ProgIdFor(extension));
        Assert.False(Make().ShowOffer);
    }

    [Fact]
    public void The_readout_follows_windows_after_coming_back()
    {
        var player = Make();
        Assert.Equal($"PLAYS {MediaTypes.All.Count} TYPES · NOT YET YOUR DEFAULT", player.StateText);
        player.ChooseCommand.Execute(null);
        _registry.Choose(".mp4", FileAssociations.VideoProgId);
        Assert.Equal($"PLAYS {MediaTypes.All.Count} TYPES · NOT YET YOUR DEFAULT", player.StateText);
        player.Refresh();
        Assert.Equal($"PLAYS {MediaTypes.All.Count} TYPES · DEFAULT FOR 1", player.StateText);
    }

    [Fact]
    public void Windows_10_gets_the_general_page()
    {
        Make(build: 19045).ChooseCommand.Execute(null);
        Assert.Equal(["ms-settings:defaultapps"], _launched);
    }

    [Fact]
    public void A_second_choose_only_opens_the_page()
    {
        var player = Make();
        player.ChooseCommand.Execute(null);
        player.ChooseCommand.Execute(null);
        Assert.Equal(1, _notified);
        Assert.Equal(2, _launched.Count);
    }

    [Fact]
    public void An_older_settings_file_still_gets_asked()
    {
        File.WriteAllText(_file, "{\"GuideShown\":true}");
        Assert.True(Make().ShowOffer);
    }
}
