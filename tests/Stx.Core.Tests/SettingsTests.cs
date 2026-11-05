using Stx.Core.Files;
using Stx.Core.Settings;
using Stx.Look;
using Xunit;

namespace Stx.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-settings").FullName;
    string Path_ => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_are_exactly_as_decided()
    {
        var s = new AppSettings();
        Assert.Equal(LookMode.Dark, s.LookMode);
        Assert.Equal(3, s.MaxParallel);
        Assert.False(s.ClipboardDetect);
        Assert.False(s.TrayMode);
        Assert.False(s.StartWithWindows);
        Assert.True(s.ToolUpdates);
        Assert.False(s.ReduceMotion);
        Assert.Equal(FilenameTemplate.Default, s.FilenameTemplate);
        Assert.EndsWith("STX Downloader", s.OutputFolder);
    }

    [Fact]
    public void Mode_persists_across_restart()
    {
        new SettingsStore(Path_).Save(new AppSettings { LookMode = LookMode.Light });
        Assert.Equal(LookMode.Light, new SettingsStore(Path_).Load().LookMode);
    }

    [Fact]
    public void Every_setting_persists()
    {
        var saved = new AppSettings { MaxParallel = 5, ClipboardDetect = true, TrayMode = true, StartWithWindows = true, ToolUpdates = false, ReduceMotion = true, OutputFolder = @"D:\Media", FilenameTemplate = "{artist} {title}" };
        new SettingsStore(Path_).Save(saved);
        Assert.Equal(saved, new SettingsStore(Path_).Load());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mode_ignores_windows_theme(bool windowsIsDark)
    {
        new SettingsStore(Path_).Save(new AppSettings { LookMode = LookMode.Light });
        Assert.Equal(LookMode.Light, LookStartup.Choose(new SettingsStore(Path_).Load(), windowsIsDark));
    }

    [Fact]
    public void First_launch_is_black_whatever_windows_uses() =>
        Assert.Equal(LookMode.Dark, LookStartup.Choose(new SettingsStore(Path_).Load(), windowsIsDark: false));

    [Theory]
    [InlineData("")]
    [InlineData("not json {{{")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void Corrupt_settings_file_falls_back_to_defaults(string contents)
    {
        File.WriteAllText(Path_, contents);
        Assert.Equal(new AppSettings(), new SettingsStore(Path_).Load());
    }

    [Fact]
    public void A_missing_file_means_defaults() => Assert.Equal(new AppSettings(), new SettingsStore(Path_).Load());

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(99, AppSettings.MostParallel)]
    public void Silly_values_in_the_file_are_brought_into_range(int stored, int expected)
    {
        File.WriteAllText(Path_, $$"""{"MaxParallel":{{stored}}}""");
        Assert.Equal(expected, new SettingsStore(Path_).Load().MaxParallel);
    }

    [Fact]
    public void Performance_defaults_to_auto_and_survives_a_bad_value()
    {
        Assert.Equal(Stx.Core.Playback.PlaybackPerformance.Auto, new AppSettings().Performance);
        File.WriteAllText(Path_, """{"Performance":99}""");
        Assert.Equal(Stx.Core.Playback.PlaybackPerformance.Auto, new SettingsStore(Path_).Load().Performance);
        File.WriteAllText(Path_, """{"Performance":3}""");
        Assert.Equal(Stx.Core.Playback.PlaybackPerformance.Max, new SettingsStore(Path_).Load().Performance);
    }

    [Fact]
    public void A_blank_output_folder_and_template_fall_back()
    {
        File.WriteAllText(Path_, """{"OutputFolder":"  ","FilenameTemplate":""}""");
        var s = new SettingsStore(Path_).Load();
        Assert.Equal(new AppSettings().OutputFolder, s.OutputFolder);
        Assert.Equal(FilenameTemplate.Default, s.FilenameTemplate);
    }

    [Fact]
    public void Settings_from_a_newer_version_still_load()
    {
        File.WriteAllText(Path_, """{"MaxParallel":4,"SomethingNew":true}""");
        Assert.Equal(4, new SettingsStore(Path_).Load().MaxParallel);
    }

    [Fact]
    public void A_save_never_leaves_a_half_written_file()
    {
        var store = new SettingsStore(Path_);
        store.Save(new AppSettings { MaxParallel = 2 });
        store.Save(new AppSettings { MaxParallel = 4 });
        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void A_save_creates_its_folder()
    {
        string nested = Path.Combine(_dir, "a", "b", "settings.json");
        new SettingsStore(nested).Save(new AppSettings());
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Settings_live_in_local_app_data() =>
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsStore.DefaultPath());
}

public class LinkDetectorTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("http://example.com/video")]
    [InlineData("  https://example.com/v  ")]
    public void Web_links_are_links(string text) => Assert.True(LinkDetector.IsMediaLink(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello world")]
    [InlineData("just some notes about https://example.com/v in a sentence")]
    [InlineData("https://example.com/a\nhttps://example.com/b")]
    [InlineData("C:\\Users\\me\\song.mp3")]
    [InlineData("file:///C:/song.mp3")]
    [InlineData("ftp://example.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("https://localhost/a")]
    public void LinkDetector_ignores_plain_text(string text) => Assert.False(LinkDetector.IsMediaLink(text));

    [Fact]
    public void Nothing_is_not_a_link() => Assert.False(LinkDetector.IsMediaLink(null!));
}

public class YtDlpTemplateTests
{
    [Theory]
    [InlineData("{title} [{id}]", "%(title)s [%(id)s].%(ext)s")]
    [InlineData("{artist} - {title}", "%(uploader)s - %(title)s.%(ext)s")]
    [InlineData("{date} {height}p", "%(upload_date)s %(height)sp.%(ext)s")]
    public void Tokens_become_the_tools_own_fields(string template, string expected) =>
        Assert.Equal(expected, FilenameTemplate.ToYtDlp(template));

    [Fact]
    public void An_unknown_token_stays_literal_as_it_does_on_screen() =>
        Assert.Equal("{nope} %(title)s.%(ext)s", FilenameTemplate.ToYtDlp("{nope} {title}"));

    [Fact]
    public void A_percent_sign_in_a_template_cannot_become_a_field() =>
        Assert.Equal("100%% %(title)s.%(ext)s", FilenameTemplate.ToYtDlp("100% {title}"));

    [Fact]
    public void A_template_cannot_reach_outside_the_output_folder() =>
        Assert.DoesNotContain("..", FilenameTemplate.ToYtDlp(@"..\..\{title}"));

    [Fact]
    public void A_blank_template_uses_the_default() =>
        Assert.Equal(FilenameTemplate.ToYtDlp(FilenameTemplate.Default), FilenameTemplate.ToYtDlp("   "));
}
