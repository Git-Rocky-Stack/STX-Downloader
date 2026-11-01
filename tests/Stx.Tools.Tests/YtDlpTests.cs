using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Tools;
using Xunit;

namespace Stx.Tools.Tests;

static class Fixtures
{
    public static string Dir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "fixtures", "ytdlp");
    }

    public static string Read(string name) => File.ReadAllText(Path.Combine(Dir(), name + ".txt"));
}

public class RealToolOutputTests
{
    /// <summary>Captured from yt-dlp 2026.10.26 against the real sites, so these words are what the tool truly says.</summary>
    [Theory]
    [InlineData("unsupported-chevrolet", ErrorKind.UnsupportedSite)]
    [InlineData("unavailable-youtube", ErrorKind.PrivateOrUnavailable)]
    [InlineData("private-playlist", ErrorKind.PrivateOrUnavailable)]
    [InlineData("http-404", ErrorKind.PrivateOrUnavailable)]
    [InlineData("http-401", ErrorKind.SignInNeeded)]
    [InlineData("http-403", ErrorKind.LinkExpired)]
    [InlineData("http-410", ErrorKind.LinkExpired)]
    [InlineData("network-dns", ErrorKind.Network)]
    [InlineData("output-bad-drive", ErrorKind.OutputFailure)]
    [InlineData("format-not-available", ErrorKind.ToolFailure)]
    public void Real_failures_are_classified(string fixture, ErrorKind expected) =>
        Assert.Equal(expected, ErrorClassifier.Classify(Fixtures.Read(fixture), 1));

    [Fact]
    public void Fixtures_hold_no_personal_paths()
    {
        foreach (string file in Directory.GetFiles(Fixtures.Dir()))
            Assert.DoesNotContain(@"C:\Users\User", File.ReadAllText(file));
    }

    [Fact]
    public void Progress_parses_fixture_lines()
    {
        var lines = Fixtures.Read("progress-real").Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var parsed = lines.Where(l => l.StartsWith("[download]") && l.Contains('%')).ToArray();
        Assert.True(parsed.Length > 20, "the fixture holds real progress");
        foreach (string line in parsed)
        {
            Assert.True(YtDlpProgress.TryParse(line, out var progress), line);
            Assert.InRange(progress.Percent, 0, 100);
        }
    }

    [Fact]
    public void Lines_that_are_not_progress_do_not_parse()
    {
        Assert.False(YtDlpProgress.TryParse(@"[download] Destination: D:\Media\Song 100% live.mp4", out _));
        Assert.False(YtDlpProgress.TryParse("[youtube] abc: Downloading webpage", out _));
        Assert.False(YtDlpProgress.TryParse("", out _));
    }

    [Fact]
    public void Progress_reads_its_parts()
    {
        Assert.True(YtDlpProgress.TryParse("[download]  13.6% of   29.34MiB at    4.38MiB/s ETA 00:05", out var p));
        Assert.Equal(13.6, p.Percent);
        Assert.Equal("29.34MiB", p.Size);
        Assert.Equal("4.38MiB/s", p.Speed);
        Assert.Equal("00:05", p.Eta);
    }

    [Fact]
    public void Progress_copes_with_unknowns_and_estimates()
    {
        Assert.True(YtDlpProgress.TryParse("[download]   5.0% of ~  10.00MiB at  Unknown B/s ETA Unknown", out var p));
        Assert.Equal("10.00MiB", p.Size);
        Assert.Null(p.Speed);
        Assert.Null(p.Eta);
    }
}

public class YtDlpArgsTests
{
    static DownloadRequest Request(DownloadMode mode = DownloadMode.Best, int? height = null) =>
        new("https://www.youtube.com/watch?v=abc", @"D:\Media", mode, height);

    static string Join(IEnumerable<string> args) => string.Join(' ', args);

    static string After(IReadOnlyList<string> args, string flag)
    {
        int at = args.ToList().IndexOf(flag);
        Assert.True(at >= 0, $"{flag} missing");
        return args[at + 1];
    }

    [Fact]
    public void Default_keeps_original_with_no_recode()
    {
        var args = YtDlpArgs.ForDownload(Request());
        Assert.DoesNotContain("--recode-video", args);
        Assert.DoesNotContain("--audio-format", args);
        Assert.DoesNotContain("-x", args);
        Assert.Equal("bv*+ba/b", After(args, "-f"));
        Assert.Equal("8", After(args, "-N"));
    }

    [Fact]
    public void Mkv_when_streams_do_not_fit_mp4() =>
        Assert.Equal("mp4/mkv", After(YtDlpArgs.ForDownload(Request()), "--merge-output-format"));

    [Theory]
    [InlineData(DownloadMode.Best, 1080, "bv*[height<=?1080]+ba/b[height<=?1080]")]
    [InlineData(DownloadMode.Best, 2160, "bv*[height<=?2160]+ba/b[height<=?2160]")]
    [InlineData(DownloadMode.AudioOnly, null, "ba/b")]
    [InlineData(DownloadMode.VideoOnly, null, "bv*")]
    [InlineData(DownloadMode.VideoOnly, 720, "bv*[height<=?720]")]
    public void Chosen_resolution_audio_only_and_video_only_map_to_formats(DownloadMode mode, int? height, string format) =>
        Assert.Equal(format, After(YtDlpArgs.ForDownload(Request(mode, height)), "-f"));

    [Fact]
    public void Users_own_yt_dlp_config_can_never_change_a_download() =>
        Assert.Equal("--ignore-config", YtDlpArgs.ForDownload(Request())[0]);

    [Fact]
    public void Existing_files_are_never_overwritten_and_partials_resume()
    {
        var args = YtDlpArgs.ForDownload(Request());
        Assert.Contains("--no-overwrites", args);
        Assert.Contains("--continue", args);
        Assert.DoesNotContain("--force-overwrites", args);
    }

    [Fact]
    public void Progress_prints_one_line_at_a_time_and_the_final_path_is_marked()
    {
        var args = YtDlpArgs.ForDownload(Request());
        Assert.Contains("--newline", args);
        Assert.Contains($"after_move:{YtDlpArgs.FileMarker}%(filepath)s", args);
    }

    [Fact]
    public void Long_titles_are_trimmed_to_fit_the_output_folder()
    {
        int Trim(string folder) => int.Parse(After(YtDlpArgs.ForDownload(new DownloadRequest("https://x/y", folder, DownloadMode.Best, null)), "--trim-filenames"));
        string shallow = @"D:\Media", deep = @"D:\" + new string('f', 150);
        Assert.Equal(240 - shallow.Length - 1 - 16, Trim(shallow));
        Assert.True(Trim(deep) < Trim(shallow));
        Assert.Equal(32, Trim(@"D:\" + new string('f', 400)));
    }

    // Without this, a real run saved Unicode names correctly but printed the path with those characters
    // missing, so the app couldn't find a file it had just saved.
    [Fact]
    public void Tool_output_is_utf8_so_unicode_paths_come_back_intact()
    {
        Assert.Equal("utf-8", After(YtDlpArgs.ForDownload(Request()), "--encoding"));
        Assert.Equal("utf-8", After(YtDlpArgs.ForEnumerate("https://x/y"), "--encoding"));
        Assert.Equal("utf-8", After(YtDlpArgs.ForFormats("https://x/y"), "--encoding"));
    }

    [Fact]
    public void Tools_are_located_when_given()
    {
        var args = YtDlpArgs.ForDownload(Request() with { Tools = new ToolPaths(@"C:\t\ffmpeg.exe", @"C:\t\deno.exe") });
        Assert.Equal(@"C:\t\ffmpeg.exe", After(args, "--ffmpeg-location"));
        Assert.Equal(@"deno:C:\t\deno.exe", After(args, "--js-runtimes"));
    }

    [Fact]
    public void Args_never_contain_cookie_or_header_values()
    {
        string root = Directory.CreateTempSubdirectory("stx-args").FullName;
        using (var credentials = CredentialFile.Create("# Netscape\nCOOKIEVALUE", new Dictionary<string, string> { ["Referer"] = "HEADERVALUE" }, "https://cdn/x.m3u8?token=TOKENVALUE", root))
        {
            var args = YtDlpArgs.ForDownload(new DownloadRequest(null, @"D:\Media", DownloadMode.Best, null) { ConfigPath = credentials.ConfigPath });
            string joined = Join(args);
            Assert.DoesNotContain("COOKIEVALUE", joined);
            Assert.DoesNotContain("HEADERVALUE", joined);
            Assert.DoesNotContain("TOKENVALUE", joined);
            Assert.Equal(credentials.ConfigPath, After(args, "--config-locations"));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public void A_link_cannot_be_read_as_an_option()
    {
        var args = YtDlpArgs.ForDownload(Request() with { Url = "--exec=calc" });
        Assert.Equal("--", args[^2]);
        Assert.Equal("--exec=calc", args[^1]);
    }

    [Fact]
    public void Enumeration_lists_without_downloading()
    {
        var args = YtDlpArgs.ForEnumerate("https://www.youtube.com/playlist?list=PL1");
        Assert.Contains("-J", args);
        Assert.Contains("--flat-playlist", args);
        Assert.Equal("--", args[^2]);
    }

    [Fact]
    public void Format_listing_reads_one_video()
    {
        var args = YtDlpArgs.ForFormats("https://www.youtube.com/watch?v=abc&list=PL1");
        Assert.Contains("-J", args);
        Assert.Contains("--no-playlist", args);
        Assert.DoesNotContain("--flat-playlist", args);
    }
}

public class PlaylistTests
{
    const string Flat = """
        {"id":"PL1","title":"Mix","_type":"playlist","entries":[
          {"id":"a1","title":"Song One","url":"https://www.youtube.com/watch?v=a1","availability":null},
          {"id":"a2","title":"[Private video]","url":"https://www.youtube.com/watch?v=a2","availability":null},
          {"id":"a3","title":"[Deleted video]","url":"https://www.youtube.com/watch?v=a3"},
          {"id":"a4","title":"Members track","url":"https://www.youtube.com/watch?v=a4","availability":"subscriber_only"},
          null,
          {"id":"a5","title":"Song Five","url":"https://www.youtube.com/watch?v=a5","availability":"public"}
        ]}
        """;

    [Fact]
    public void Playlist_enumeration_returns_entries_and_marks_unavailable_ones()
    {
        var entries = PlaylistListing.Parse(Flat);
        Assert.Equal(["a1", "a2", "a3", "a4", "a5"], entries.Select(e => e.Id));
        Assert.Equal([true, false, false, false, true], entries.Select(e => e.Available));
        Assert.Equal("https://www.youtube.com/watch?v=a1", entries[0].Url);
    }

    [Fact]
    public void A_single_video_is_a_list_of_one() =>
        Assert.Equal("v1", Assert.Single(PlaylistListing.Parse("""{"id":"v1","title":"Only","webpage_url":"https://x/v1"}""")).Id);

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Unreadable_output_is_an_empty_list(string json) => Assert.Empty(PlaylistListing.Parse(json));
}

public sealed class CredentialFileTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-cred").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    CredentialFile Make(string? cookies = "c=1") =>
        CredentialFile.Create(cookies, new Dictionary<string, string> { ["Referer"] = "https://site/" }, "https://cdn/x.m3u8?token=T", _root);

    [Fact]
    public void Credential_file_acl_is_current_user_only()
    {
        using var credentials = Make();
        foreach (string path in new[] { credentials.ConfigPath, credentials.CookiePath! })
        {
            var security = new FileInfo(path).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected, "must not inherit from the folder");
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            var rule = Assert.Single(rules);
            Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }
    }

    [Fact]
    public void Credential_file_deleted_on_dispose()
    {
        string config, cookies;
        using (var credentials = Make())
        {
            config = credentials.ConfigPath;
            cookies = credentials.CookiePath!;
            Assert.True(File.Exists(config) && File.Exists(cookies));
        }
        Assert.False(File.Exists(config));
        Assert.False(File.Exists(cookies));
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var credentials = Make();
        credentials.Dispose();
        credentials.Dispose();
    }

    [Fact]
    public void Leftovers_deleted_at_startup()
    {
        Make();
        Assert.NotEmpty(Directory.GetFiles(_root));
        CredentialFile.DeleteLeftovers(_root);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void Leftovers_cleanup_with_no_folder_is_harmless() =>
        CredentialFile.DeleteLeftovers(Path.Combine(_root, "never-made"));

    [Fact]
    public void Config_points_at_the_cookies_and_holds_headers_and_the_link()
    {
        using var credentials = Make();
        string config = File.ReadAllText(credentials.ConfigPath);
        Assert.Contains($"--cookies \"{credentials.CookiePath!.Replace('\\', '/')}\"", config);
        Assert.Contains("--add-headers \"Referer:https://site/\"", config);
        Assert.Contains("\"https://cdn/x.m3u8?token=T\"", config);
    }

    [Fact]
    public void Cookies_get_the_header_the_tool_requires()
    {
        using var credentials = Make("example.com\tTRUE\t/\tFALSE\t0\ta\tb");
        Assert.StartsWith("# Netscape HTTP Cookie File", File.ReadAllText(credentials.CookiePath!));
    }

    [Fact]
    public void No_cookies_means_no_cookie_file()
    {
        using var credentials = Make(null);
        Assert.Null(credentials.CookiePath);
        Assert.DoesNotContain("--cookies", File.ReadAllText(credentials.ConfigPath));
    }

    [Fact]
    public void A_header_value_cannot_add_lines_or_break_out_of_its_quotes()
    {
        using var credentials = CredentialFile.Create(null, new Dictionary<string, string> { ["X"] = "a\"b\r\n--exec calc" }, "https://cdn/x", _root);
        string[] lines = File.ReadAllLines(credentials.ConfigPath);
        Assert.DoesNotContain(lines, l => l.StartsWith("--exec"));
        Assert.Contains(lines, l => l.Contains("\\\"b"));
    }

    [Fact]
    public void Credential_files_have_unguessable_names()
    {
        using var a = Make();
        using var b = Make();
        Assert.NotEqual(a.ConfigPath, b.ConfigPath);
    }
}

sealed class FakeProcess(Action<Action<string>, string, IReadOnlyList<string>> script, int exit = 0) : IProcessRunner
{
    public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
    {
        script(onLine, path, args);
        return Task.FromResult(exit);
    }
}

public sealed class YtDlpRunnerTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-run").FullName;
    readonly CaptureVault _vault = new();

    public void Dispose() => Directory.Delete(_root, true);

    static Job LinkJob(long id = 1) => new(id, 1, JobKind.Link, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch);
    static Job CapturedJob(long id = 1) => LinkJob(id) with { Kind = JobKind.Captured };

    DownloadRequest Req(Job _) => new("https://x/y", _root, DownloadMode.Best, null);

    YtDlpRunner Runner(IProcessRunner process) => new(process, "yt-dlp.exe", Req, _vault, _root);

    [Fact]
    public async Task Clean_exit_is_success()
    {
        var outcome = await Runner(new FakeProcess((_, _, _) => { })).RunAsync(LinkJob(), default);
        Assert.True(outcome.IsSuccess);
    }

    [Fact]
    public async Task Disk_full_output_is_OutputFailure()
    {
        var process = new FakeProcess((line, _, _) => line("ERROR: unable to write data: [Errno 28] No space left on device"), exit: 1);
        var outcome = await Runner(process).RunAsync(LinkJob(), default);
        Assert.False(outcome.IsSuccess);
        Assert.Equal(ErrorKind.OutputFailure, outcome.Error);
    }

    [Fact]
    public async Task Partial_file_is_left_for_retry_when_a_job_fails()
    {
        string partial = Path.Combine(_root, "song.mp4.part");
        File.WriteAllText(partial, "half");
        var process = new FakeProcess((line, _, _) => line("ERROR: unable to write data: [Errno 28] No space left on device"), exit: 1);
        await Runner(process).RunAsync(LinkJob(), default);
        Assert.True(File.Exists(partial));
    }

    [Fact]
    public async Task A_gone_output_folder_fails_without_starting_the_tool()
    {
        bool started = false;
        var process = new FakeProcess((_, _, _) => started = true);
        var runner = new YtDlpRunner(process, "yt-dlp.exe", _ => new DownloadRequest("https://x/y", @"Q:\pulled\usb\drive", DownloadMode.Best, null), _vault, _root);
        var outcome = await runner.RunAsync(LinkJob(), default);
        Assert.Equal(ErrorKind.OutputFailure, outcome.Error);
        Assert.False(started);
    }

    [Fact]
    public async Task Every_line_is_scrubbed_before_it_reaches_the_log()
    {
        var logged = new List<string>();
        var process = new FakeProcess((line, _, _) => line("GET https://cdn/x?token=SECRET Cookie: a=SECRET"));
        var runner = Runner(process);
        runner.LogLine += (_, entry) => logged.Add(entry.Text);
        await runner.RunAsync(LinkJob(), default);
        Assert.NotEmpty(logged);
        Assert.DoesNotContain(logged, l => l.Contains("SECRET"));
    }

    [Fact]
    public async Task Progress_is_reported()
    {
        var seen = new List<double>();
        var process = new FakeProcess((line, _, _) => line("[download]  13.6% of   29.34MiB at    4.38MiB/s ETA 00:05"));
        var runner = Runner(process);
        runner.ProgressChanged += (_, p) => seen.Add(p.Progress.Percent);
        await runner.RunAsync(LinkJob(), default);
        Assert.Equal([13.6], seen);
    }

    [Fact]
    public async Task The_final_file_is_reported_with_its_job()
    {
        (long, string)? seen = null;
        var process = new FakeProcess((line, _, _) => line(YtDlpArgs.FileMarker + @"D:\Media\Song [abc].mp4"));
        var runner = Runner(process);
        runner.FileSaved += (_, saved) => seen = (saved.JobId, saved.Path);
        await runner.RunAsync(LinkJob(7), default);
        Assert.Equal((7L, @"D:\Media\Song [abc].mp4"), seen);
    }

    [Fact]
    public async Task A_captured_job_passes_its_secrets_only_through_the_protected_file()
    {
        _vault.Put(1, new CaptureSecrets("https://cdn/x.m3u8?token=TOKENVALUE", new Dictionary<string, string> { ["Referer"] = "HEADERVALUE" }, "COOKIEVALUE"));
        string? config = null, joined = null;
        bool existedDuringRun = false;
        var process = new FakeProcess((_, _, args) =>
        {
            joined = string.Join(' ', args);
            config = args[args.ToList().IndexOf("--config-locations") + 1];
            existedDuringRun = File.Exists(config);
        });
        var runner = new YtDlpRunner(process, "yt-dlp.exe", _ => new DownloadRequest(null, _root, DownloadMode.Best, null), _vault, _root);

        var outcome = await runner.RunAsync(CapturedJob(), default);

        Assert.True(outcome.IsSuccess);
        Assert.True(existedDuringRun);
        Assert.DoesNotContain("TOKENVALUE", joined);
        Assert.DoesNotContain("HEADERVALUE", joined);
        Assert.DoesNotContain("COOKIEVALUE", joined);
        Assert.False(File.Exists(config), "deleted when the run ends");
    }

    [Fact]
    public async Task Credential_files_are_deleted_even_when_the_tool_fails()
    {
        _vault.Put(1, new CaptureSecrets("https://cdn/x", new Dictionary<string, string>(), "c=1"));
        var process = new FakeProcess((_, _, _) => throw new InvalidOperationException("crash"));
        var runner = new YtDlpRunner(process, "yt-dlp.exe", _ => new DownloadRequest(null, _root, DownloadMode.Best, null), _vault, _root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(CapturedJob(), default));
        Assert.Empty(Directory.GetFiles(_root, "stx-*"));
    }

    [Fact]
    public async Task A_captured_job_with_no_secrets_is_an_expired_link()
    {
        var runner = new YtDlpRunner(new FakeProcess((_, _, _) => { }), "yt-dlp.exe", _ => new DownloadRequest(null, _root, DownloadMode.Best, null), _vault, _root);
        Assert.Equal(ErrorKind.LinkExpired, (await runner.RunAsync(CapturedJob(), default)).Error);
    }
}

public class ProcessRunnerTests
{
    [Fact]
    public async Task Reads_both_streams_and_returns_the_exit_code()
    {
        var lines = new List<string>();
        int exit = await new ProcessRunner().RunAsync("cmd.exe", ["/c", "echo out&& echo err 1>&2&& exit /b 7"], l => { lock (lines) lines.Add(l.Trim()); }, default);
        Assert.Equal(7, exit);
        Assert.Contains("out", lines);
        Assert.Contains("err", lines);
    }

    [Fact]
    public async Task An_argument_with_spaces_and_shell_characters_stays_one_argument()
    {
        var lines = new List<string>();
        await new ProcessRunner().RunAsync("cmd.exe", ["/c", "echo", "a b & echo injected"], l => { lock (lines) lines.Add(l); }, default);
        Assert.DoesNotContain(lines, l => l.Trim() == "injected");
    }

    [Fact]
    public async Task Canceling_stops_the_whole_process_tree_quickly()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessRunner().RunAsync("cmd.exe", ["/c", "ping -n 30 127.0.0.1"], _ => { }, cancel.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Canceling_leaves_no_process_behind()
    {
        int before = Process.GetProcessesByName("PING").Length;
        using var cancel = new CancellationTokenSource();
        var run = new ProcessRunner().RunAsync("cmd.exe", ["/c", "ping -n 40 127.0.0.1"], _ => cancel.Cancel(), cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        int after = int.MaxValue;
        for (int i = 0; i < 50 && after > before; i++)
        {
            await Task.Delay(100);
            after = Process.GetProcessesByName("PING").Length;
        }
        Assert.True(after <= before, $"{after} ping processes left, {before} before");
    }

    [Fact]
    public async Task A_missing_program_is_reported_not_swallowed() =>
        await Assert.ThrowsAnyAsync<Exception>(() => new ProcessRunner().RunAsync(@"C:\no\such\tool.exe", [], _ => { }, default));
}
