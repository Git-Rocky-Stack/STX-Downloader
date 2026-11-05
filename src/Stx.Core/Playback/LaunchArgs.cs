namespace Stx.Core.Playback;

/// <summary>Which file a launch asks STX to play: Explorer's "Open with", a double-click once STX is the default player, or a second launch.</summary>
public static class LaunchArgs
{
    /// <summary>
    /// The first argument that isn't a flag and names a file that exists, or null. Pass the arguments after the
    /// program's own path. Flags (--gallery=…, --hidden, …) never count, whatever they hold.
    /// </summary>
    public static string? FileToOpen(IReadOnlyList<string> args, Func<string, bool> exists) =>
        args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(a) && exists(a));
}
