using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>Which file a launch asks STX to play. The arguments are the ones after the program's own path.</summary>
public class LaunchArgsTests
{
    static readonly HashSet<string> OnDisk = new(StringComparer.OrdinalIgnoreCase) { @"D:\Films\a.mkv", @"D:\Music\b.flac", @"D:\Фильмы\My Movie (2019).mkv" };

    [Fact]
    public void The_first_existing_non_flag_argument_wins() =>
        Assert.Equal(@"D:\Films\a.mkv", LaunchArgs.FileToOpen(["--pointer-light", @"D:\gone.mkv", @"D:\Films\a.mkv", @"D:\Music\b.flac"], OnDisk.Contains));

    [Fact]
    public void Gallery_and_hidden_flags_are_ignored() =>
        Assert.Null(LaunchArgs.FileToOpen(["--gallery=player", "--hidden"], _ => true));

    [Fact]
    public void A_missing_file_gives_null() =>
        Assert.Null(LaunchArgs.FileToOpen([@"D:\gone.mkv"], OnDisk.Contains));

    [Fact]
    public void Nothing_to_open_gives_null() => Assert.Null(LaunchArgs.FileToOpen([], OnDisk.Contains));

    [Fact]
    public void Unicode_and_spaced_paths_survive() =>
        Assert.Equal(@"D:\Фильмы\My Movie (2019).mkv", LaunchArgs.FileToOpen([@"D:\Фильмы\My Movie (2019).mkv"], OnDisk.Contains));
}
