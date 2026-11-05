using System.Text;

namespace Stx.Core.Library;

/// <summary>A value under HKEY_CURRENT_USER. A null name is the key's default value.</summary>
public sealed record RegistryValue(string Key, string? Name, string Value);

/// <summary>The part of the current user's registry STX reads and writes. Nothing outside the user's own hive.</summary>
public interface IUserRegistry
{
    string? Read(string key, string? name);
    void Write(string key, string? name, string value);
}

/// <summary>How many of the types STX registers Windows actually opens with STX, read from Windows, never assumed.</summary>
public sealed record DefaultState(int Ours, int Total)
{
    public bool IsAll => Total > 0 && Ours == Total;
    public bool IsNone => Ours == 0;

    public string Text => IsAll ? "STX IS YOUR PLAYER FOR VIDEOS AND MUSIC"
        : IsNone ? "STX ISN'T YOUR DEFAULT PLAYER"
        : $"STX OPENS {Ours} OF {Total} TYPES";
}

/// <summary>
/// STX offered as a player for every type the parity corpus proves it plays, per user: a ProgID for video and one for
/// audio, Capabilities and RegisteredApplications (so Windows lists STX in Default Apps), and "Open with" entries.
/// Windows doesn't let an app make itself the default. The user chooses STX in Windows, once.
/// The installer's [Registry] lines are generated from the same plan, and a test keeps them equal.
/// </summary>
public static class FileAssociations
{
    public const string AppName = "STX Downloader";
    public const string ExeName = "STXDownloader.exe";
    public const string VideoProgId = "STXDownloader.Video";
    public const string AudioProgId = "STXDownloader.Audio";
    public const string CapabilitiesKey = @"Software\STXDownloader\Capabilities";
    const string Classes = @"Software\Classes";
    const string RegisteredApplications = @"Software\RegisteredApplications";

    public static string ProgIdFor(string extension) => MediaTypes.KindOf("x" + extension) == MediaKind.Audio ? AudioProgId : VideoProgId;

    /// <summary>Every value STX writes, for STX at <paramref name="exePath"/>.</summary>
    public static IReadOnlyList<RegistryValue> Plan(string exePath)
    {
        string open = $"\"{exePath}\" \"%1\"";
        string icon = $"\"{exePath}\",0";
        var values = new List<RegistryValue>();
        foreach (var (progId, name) in new[] { (VideoProgId, "STX video"), (AudioProgId, "STX audio") })
        {
            values.Add(new($@"{Classes}\{progId}", null, name));
            values.Add(new($@"{Classes}\{progId}", "FriendlyTypeName", name));
            values.Add(new($@"{Classes}\{progId}\DefaultIcon", null, icon));
            values.Add(new($@"{Classes}\{progId}\shell\open\command", null, open));
        }
        values.Add(new($@"{Classes}\Applications\{ExeName}", "FriendlyAppName", AppName));
        values.Add(new($@"{Classes}\Applications\{ExeName}\shell\open\command", null, open));
        values.Add(new(CapabilitiesKey, "ApplicationName", AppName));
        values.Add(new(CapabilitiesKey, "ApplicationDescription", "Plays your videos and music on VLC's engine, and tells you the truth about their quality."));
        values.Add(new(CapabilitiesKey, "ApplicationIcon", icon));
        foreach (string extension in MediaTypes.All)
        {
            values.Add(new($@"{CapabilitiesKey}\FileAssociations", extension, ProgIdFor(extension)));
            values.Add(new($@"{Classes}\{extension}\OpenWithProgids", ProgIdFor(extension), ""));
            values.Add(new($@"{Classes}\Applications\{ExeName}\SupportedTypes", extension, ""));
        }
        values.Add(new(RegisteredApplications, AppName, CapabilitiesKey));
        return values;
    }

    /// <summary>Writes whatever of the plan isn't already there. True when anything was written.</summary>
    public static bool Register(IUserRegistry registry, string exePath)
    {
        bool wrote = false;
        foreach (var value in Plan(exePath))
        {
            if (registry.Read(value.Key, value.Name) == value.Value) continue;
            registry.Write(value.Key, value.Name, value.Value);
            wrote = true;
        }
        return wrote;
    }

    /// <summary>
    /// Which registered types Windows opens with STX: the ProgId of each type's user choice, the newer
    /// UserChoiceLatest key first (Windows 11 24H2 and later), then UserChoice.
    /// </summary>
    public static DefaultState Read(IUserRegistry registry)
    {
        int ours = 0;
        foreach (string extension in MediaTypes.All)
        {
            string key = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}";
            // Where Windows 11 keeps the newer choice isn't documented, so both shapes seen in the wild are read.
            string? chosen = registry.Read(key + @"\UserChoiceLatest\ProgId", "ProgId") ?? registry.Read(key + @"\UserChoiceLatest", "ProgId")
                ?? registry.Read(key + @"\UserChoice", "ProgId");
            if (chosen is VideoProgId or AudioProgId || (chosen?.EndsWith(@"\" + ExeName, StringComparison.OrdinalIgnoreCase) ?? false)
                || string.Equals(chosen, "Applications\\" + ExeName, StringComparison.OrdinalIgnoreCase))
                ours++;
        }
        return new DefaultState(ours, MediaTypes.All.Count);
    }

    /// <summary>
    /// The Default Apps page to open. Windows 11 (build 22000 and later) can open STX's own page in it; Windows 10
    /// opens the general page, where the user picks STX per type (spec assumption A6).
    /// </summary>
    public static string DefaultAppsLink(int windowsBuild) =>
        windowsBuild >= 22000 ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppName) : "ms-settings:defaultapps";

    /// <summary>The installer's [Registry] lines for this plan, with Inno Setup's {app} for the install folder.</summary>
    public static string InnoRegistry()
    {
        var text = new StringBuilder();
        text.AppendLine("; Generated from src/Stx.Core/Library/FileAssociations.cs. Don't edit by hand:");
        text.AppendLine("; FileAssociationTests.The_installer_registers_exactly_the_plan says how to make it again.");
        foreach (var value in Plan(@"{app}\" + ExeName))
        {
            // Values STX owns outright go with the key; values inside keys Windows or other apps share go alone.
            bool shared = value.Key.EndsWith("OpenWithProgids", StringComparison.Ordinal) || value.Key == RegisteredApplications;
            string name = value.Name is null ? "" : $" ValueName: \"{Inno(value.Name)}\";";
            text.AppendLine($"Root: HKCU; Subkey: \"{Inno(value.Key)}\";{name} ValueType: string; ValueData: \"{Inno(value.Value)}\"; Flags: {(shared ? "uninsdeletevalue" : "uninsdeletekey")}");
        }
        text.AppendLine("Root: HKCU; Subkey: \"Software\\STXDownloader\"; Flags: uninsdeletekeyifempty");
        return text.ToString();
    }

    // Inno doubles quotes inside a quoted parameter. {app} is left for Inno to fill in.
    static string Inno(string text) => text.Replace("\"", "\"\"", StringComparison.Ordinal);
}
