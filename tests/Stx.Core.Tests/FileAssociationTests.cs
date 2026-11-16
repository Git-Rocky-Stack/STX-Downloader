using Stx.Core.Library;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>A registry held in memory, keyed the way Windows' is: without regard to case.</summary>
sealed class MemoryRegistry : IUserRegistry
{
    public readonly Dictionary<(string Key, string Name), string> Values = new(new KeyComparer());
    public int Writes;

    public string? Read(string key, string? name) => Values.TryGetValue((key, name ?? ""), out var value) ? value : null;

    public void Write(string key, string? name, string value)
    {
        Writes++;
        Values[(key, name ?? "")] = value;
    }

    public void Choose(string extension, string progId) =>
        Values[($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice", "ProgId")] = progId;

    /// <summary>Classes registered for every user, as VLC's installer registers its own.</summary>
    public readonly HashSet<string> MachineClasses = new(StringComparer.OrdinalIgnoreCase);

    // A class is registered for this user once something is written under its key in Software\Classes.
    public bool ClassExists(string progId) =>
        MachineClasses.Contains(progId) || Values.Keys.Any(v => IsUnder(v.Key, $@"Software\Classes\{progId}"));

    static bool IsUnder(string key, string root) =>
        key.Equals(root, StringComparison.OrdinalIgnoreCase) || key.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);

    sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) v) => HashCode.Combine(v.Item1.ToLowerInvariant(), v.Item2.ToLowerInvariant());
    }
}

public class FileAssociationTests
{
    const string Exe = @"C:\Users\Rocky\AppData\Local\Programs\STX Downloader\STXDownloader.exe";

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void Every_media_type_is_offered_with_the_right_progid()
    {
        var plan = FileAssociations.Plan(Exe);
        foreach (string extension in MediaTypes.All)
        {
            string progId = MediaTypes.KindOf("x" + extension) == MediaKind.Audio ? FileAssociations.AudioProgId : FileAssociations.VideoProgId;
            Assert.Contains(new RegistryValue($@"{FileAssociations.CapabilitiesKey}\FileAssociations", extension, progId), plan);
            Assert.Contains(new RegistryValue($@"Software\Classes\{extension}\OpenWithProgids", progId, ""), plan);
        }
        Assert.Equal(FileAssociations.AudioProgId, FileAssociations.ProgIdFor(".flac"));
        Assert.Equal(FileAssociations.VideoProgId, FileAssociations.ProgIdFor(".mkv"));
    }

    // Done when: double-clicking a registered type opens the console and plays it. Windows passes the path as one argument.
    [Fact]
    public void Opening_passes_the_file_as_one_quoted_argument()
    {
        var plan = FileAssociations.Plan(Exe);
        Assert.Contains(new RegistryValue(@"Software\Classes\STXDownloader.Video\shell\open\command", null, $"\"{Exe}\" \"%1\""), plan);
        Assert.Contains(new RegistryValue(@"Software\Classes\STXDownloader.Audio\shell\open\command", null, $"\"{Exe}\" \"%1\""), plan);
    }

    [Fact]
    public void Windows_lists_STX_in_default_apps()
    {
        var plan = FileAssociations.Plan(Exe);
        Assert.Contains(new RegistryValue(@"Software\RegisteredApplications", "STX Downloader", FileAssociations.CapabilitiesKey), plan);
        Assert.Contains(plan, v => v.Key == FileAssociations.CapabilitiesKey && v.Name == "ApplicationName");
    }

    // Spec anti-goal: STX setting itself as default without the user's say. The plan never touches a user choice.
    [Fact]
    public void Nothing_is_made_the_default()
    {
        Assert.DoesNotContain(FileAssociations.Plan(Exe), v => v.Key.Contains("UserChoice", StringComparison.OrdinalIgnoreCase) || v.Key.Contains("FileExts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FileAssociations.Plan(Exe), v => v.Key.StartsWith(@"Software\Classes\.", StringComparison.Ordinal) && v.Name is null);
    }

    [Fact]
    public void Everything_stays_in_the_users_own_classes_and_software_keys() =>
        Assert.All(FileAssociations.Plan(Exe), v => Assert.StartsWith(@"Software\", v.Key));

    [Fact]
    public void Registering_twice_writes_once()
    {
        var registry = new MemoryRegistry();
        Assert.True(FileAssociations.Register(registry, Exe));
        int writes = registry.Writes;
        Assert.False(FileAssociations.Register(registry, Exe));
        Assert.Equal(writes, registry.Writes);
    }

    [Fact]
    public void A_moved_install_is_registered_again()
    {
        var registry = new MemoryRegistry();
        FileAssociations.Register(registry, Exe);
        Assert.True(FileAssociations.Register(registry, @"D:\Portable\STX\STXDownloader.exe"));
        Assert.Equal("\"D:\\Portable\\STX\\STXDownloader.exe\" \"%1\"", registry.Read(@"Software\Classes\STXDownloader.Video\shell\open\command", null));
    }

    [Fact]
    public void The_state_is_read_from_the_users_choices()
    {
        var registry = new MemoryRegistry();
        FileAssociations.Register(registry, Exe); // as the installer does: Windows offers STX only once it's registered
        Assert.True(FileAssociations.Read(registry).IsNone);

        registry.Choose(".mp4", FileAssociations.VideoProgId);
        registry.Choose(".mp3", FileAssociations.AudioProgId);
        registry.Choose(".mkv", "VLC.mkv");
        var some = FileAssociations.Read(registry);
        Assert.Equal(2, some.Ours);
        Assert.Equal($"PLAYS {MediaTypes.All.Count} TYPES · DEFAULT FOR 2", some.Text);
        Assert.DoesNotContain("OPENS", some.Text, StringComparison.Ordinal);

        foreach (string extension in MediaTypes.All) registry.Choose(extension, FileAssociations.ProgIdFor(extension));
        Assert.True(FileAssociations.Read(registry).IsAll);
        Assert.Equal($"DEFAULT PLAYER FOR ALL {MediaTypes.All.Count} TYPES", FileAssociations.Read(registry).Text);
    }

    [Fact]
    public void Windows_11s_newer_choice_wins_over_the_older_one()
    {
        var registry = new MemoryRegistry();
        FileAssociations.Register(registry, Exe);
        registry.MachineClasses.Add("VLC.mp4");
        registry.Choose(".mp4", "VLC.mp4");
        registry.Values[(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.mp4\UserChoiceLatest\ProgId", "ProgId")] = FileAssociations.VideoProgId;
        Assert.Equal(1, FileAssociations.Read(registry).Ours);
    }

    // Found 2026-11-15 on build 26300.9974: Media Player was no longer installed, yet 36 of these types still named its
    // class as the user's choice, and Windows opened them with something else. Windows doesn't open a type with an app
    // whose class isn't registered, so a choice naming one is no choice.
    [Fact]
    public void A_choice_naming_an_app_that_is_gone_is_no_choice()
    {
        var registry = new MemoryRegistry();
        const string latest = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.flac\UserChoiceLatest\ProgId";
        registry.Values[(latest, "ProgId")] = "AppXqj98qxeaynz6dv4459ayz6bnqxbyaqcs";
        Assert.Null(FileAssociations.ChosenProgId(registry, ".flac"));

        registry.MachineClasses.Add("VLC.flac");
        registry.Values[(latest, "ProgId")] = "VLC.flac";
        Assert.Equal("VLC.flac", FileAssociations.ChosenProgId(registry, ".flac"));
    }

    [Fact]
    public void STX_counts_as_the_player_only_while_it_is_registered()
    {
        var registry = new MemoryRegistry();
        registry.Choose(".mp4", FileAssociations.VideoProgId);
        Assert.Equal(0, FileAssociations.Read(registry).Ours);

        FileAssociations.Register(registry, Exe);
        Assert.Equal(1, FileAssociations.Read(registry).Ours);
    }

    // Spec assumption A6: Windows 11 can open STX's own page; Windows 10 gets the general page.
    [Theory]
    [InlineData(19045, "ms-settings:defaultapps")]
    [InlineData(22000, "ms-settings:defaultapps?registeredAppUser=STX%20Downloader")]
    [InlineData(26300, "ms-settings:defaultapps?registeredAppUser=STX%20Downloader")]
    public void Default_apps_opens_the_right_page(int build, string link) => Assert.Equal(link, FileAssociations.DefaultAppsLink(build));

    // Phase 3: the installer registers STX for every format the corpus proves it plays. One plan, two writers.
    // To make the file again: STX_WRITE_ASSOCIATIONS=1 dotnet test --project tests/Stx.Core.Tests --filter-method *The_installer_registers_exactly_the_plan
    [Fact]
    public void The_installer_registers_exactly_the_plan()
    {
        string file = Path.Combine(Root(), "tools", "installer", "associations.iss");
        string expected = FileAssociations.InnoRegistry().ReplaceLineEndings("\r\n");
        if (Environment.GetEnvironmentVariable("STX_WRITE_ASSOCIATIONS") == "1") File.WriteAllText(file, expected);
        Assert.Equal(expected, File.ReadAllText(file).ReplaceLineEndings("\r\n"));
        Assert.Contains("#include \"associations.iss\"", File.ReadAllText(Path.Combine(Root(), "tools", "installer", "STXDownloader.iss")));
    }

    [Fact]
    public void Uninstalling_takes_out_only_what_STX_added()
    {
        string inno = FileAssociations.InnoRegistry();
        foreach (string line in inno.Split('\n').Where(l => l.Contains("OpenWithProgids") || l.Contains("RegisteredApplications")))
            Assert.Contains("uninsdeletevalue", line);
        Assert.DoesNotContain(inno.Split('\n'), l => l.Contains(@"Software\Classes\.") && l.Contains("uninsdeletekey"));
    }
}
