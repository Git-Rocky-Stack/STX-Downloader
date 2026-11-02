using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Tools;
using Xunit;

namespace Stx.Tools.Tests;

/// <summary>Stands in for the internet. Anything not listed answers 404, and every request is recorded.</summary>
sealed class FakeWeb : HttpMessageHandler
{
    public readonly Dictionary<string, byte[]> Files = new();
    public readonly List<Uri> Requests = new();
    public bool Offline;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        lock (Requests) Requests.Add(request.RequestUri!);
        if (Offline) throw new HttpRequestException("no route to host");
        return Task.FromResult(Files.TryGetValue(request.RequestUri!.ToString(), out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Stands in for running a tool: says whether a given exe reports a version.</summary>
sealed class FakeTools : IProcessRunner
{
    public Func<string, bool> Works = _ => true;

    public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
    {
        if (!Works(path)) return Task.FromResult(1);
        onLine("1.2.3");
        return Task.FromResult(0);
    }
}

static class Web
{
    public static string Hash(byte[] body) => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    public static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
                using (var stream = zip.CreateEntry(name).Open())
                    stream.Write(Encoding.UTF8.GetBytes(text));
        return memory.ToArray();
    }
}

public sealed class ToolManagerTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-tools").FullName;
    readonly FakeWeb _web = new();
    readonly FakeTools _tools = new();
    readonly byte[] _ytDlp = Encoding.UTF8.GetBytes("yt-dlp v1");

    public ToolManagerTests() => Publish();

    public void Dispose() => Directory.Delete(_root, true);

    ToolManager Manager(bool updates = true) => new(new HttpClient(_web), _root, new ToolOptions(updates), _tools);

    string Installed(string name) => Path.Combine(_root, name);

    /// <summary>Puts every tool, with correct checksums, on the fake internet.</summary>
    void Publish()
    {
        PublishYtDlp(_ytDlp);

        byte[] deno = Web.Zip(("deno.exe", "deno"));
        _web.Files[ToolVersions.DenoZip] = deno;
        // The real file is PowerShell's Get-FileHash output, not the usual "hash  name" line.
        _web.Files[ToolVersions.DenoSums] = Encoding.UTF8.GetBytes($"Algorithm : SHA256\r\nHash      : {Web.Hash(deno).ToUpperInvariant()}\r\nPath      : C:\\a\\deno.zip\r\n");

        byte[] ffmpeg = Web.Zip(("ffmpeg-n9.0/bin/ffmpeg.exe", "ffmpeg"), ("ffmpeg-n9.0/bin/ffprobe.exe", "ffprobe"), ("ffmpeg-n9.0/doc/readme.txt", "docs"));
        _web.Files[FfmpegSource.Zip] = ffmpeg;
        _web.Files[FfmpegSource.Sums] = Encoding.UTF8.GetBytes($"{Web.Hash(ffmpeg)}  {FfmpegSource.ZipName}\n{new string('0', 64)}  other.zip\n");
    }

    void PublishYtDlp(byte[] body, string? claimedHash = null)
    {
        _web.Files[ToolVersions.YtDlpExe] = body;
        _web.Files[ToolVersions.YtDlpSums] = Encoding.UTF8.GetBytes($"{new string('1', 64)}  yt-dlp\n{claimedHash ?? Web.Hash(body)}  yt-dlp.exe\n");
    }

    [Fact]
    public async Task First_run_installs_every_tool()
    {
        Assert.Equal(ToolHealth.Current, await Manager().EnsureAsync());
        foreach (string exe in new[] { "yt-dlp.exe", "deno.exe", "ffmpeg.exe", "ffprobe.exe" })
            Assert.True(File.Exists(Installed(exe)), exe);
    }

    [Fact]
    public async Task Paths_point_at_the_installed_tools()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        Assert.Equal(Installed("yt-dlp.exe"), manager.YtDlpPath);
        Assert.Equal(new ToolPaths(Installed("ffmpeg.exe"), Installed("deno.exe")), manager.Paths);
    }

    [Fact]
    public async Task Only_the_needed_files_leave_the_archive()
    {
        await Manager().EnsureAsync();
        Assert.False(File.Exists(Installed("readme.txt")));
    }

    [Fact]
    public async Task A_hostile_archive_cannot_write_outside_the_tools_folder()
    {
        // These carry the very names the installer looks for, so they do get extracted.
        string nested = Path.Combine(_root, "deep", "er");
        Directory.CreateDirectory(nested);
        byte[] evil = Web.Zip(("../../ffmpeg.exe", "evil"), ("../../ffprobe.exe", "evil"));
        _web.Files[FfmpegSource.Zip] = evil;
        _web.Files[FfmpegSource.Sums] = Encoding.UTF8.GetBytes($"{Web.Hash(evil)}  {FfmpegSource.ZipName}\n");

        // The self-test runs while extracted files sit on disk, so a stray write can't hide by being moved later.
        bool strayFile = false;
        _tools.Works = _ =>
        {
            strayFile |= Directory.GetFiles(_root, "ffmpeg*").Length > 0 || Directory.GetFiles(Path.Combine(_root, "deep"), "ffmpeg*").Length > 0;
            return true;
        };

        await new ToolManager(new HttpClient(_web), nested, new ToolOptions(true), _tools).EnsureAsync();

        Assert.False(strayFile, "nothing may land outside the tools folder, even briefly");
        Assert.Empty(Directory.GetFiles(_root, "ffmpeg*"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "deep"), "ffmpeg*"));
        Assert.True(File.Exists(Path.Combine(nested, "ffmpeg.exe")), "it lands in the tools folder under its plain name");
    }

    [Fact]
    public async Task Checksum_mismatch_rejects_and_keeps_current()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("tampered"), claimedHash: new string('a', 64));

        var health = await manager.UpdateYtDlpAsync();

        Assert.Equal(ToolHealth.UpdatePending, health);
        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));
        Assert.Null(manager.PreviousYtDlpPath);
    }

    [Fact]
    public async Task A_tool_with_a_bad_checksum_is_not_installed_on_first_run()
    {
        PublishYtDlp(_ytDlp, claimedHash: new string('a', 64));
        Assert.Equal(ToolHealth.Missing, await Manager().EnsureAsync());
        Assert.False(File.Exists(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task A_tool_with_no_published_checksum_is_refused()
    {
        _web.Files.Remove(ToolVersions.YtDlpSums);
        Assert.Equal(ToolHealth.Missing, await Manager().EnsureAsync());
        Assert.False(File.Exists(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task Self_test_failure_rolls_back()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2 broken"));
        // Whatever isn't installed yet is the new download, and that's what can't report a version.
        _tools.Works = path => path == Installed("yt-dlp.exe") || !path.Contains("yt-dlp");

        var health = await manager.UpdateYtDlpAsync();

        Assert.Equal(ToolHealth.UpdatePending, health);
        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));
        Assert.Null(manager.PreviousYtDlpPath);
        Assert.Equal(["yt-dlp.exe"], Directory.GetFiles(_root, "yt-dlp*").Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_good_update_keeps_the_prior_version_alongside()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));

        Assert.Equal(ToolHealth.Current, await manager.UpdateYtDlpAsync());

        Assert.Equal("yt-dlp v2", File.ReadAllText(Installed("yt-dlp.exe")));
        Assert.Equal("yt-dlp v1", File.ReadAllText(manager.PreviousYtDlpPath!));
        Assert.True(manager.UpdateUnproven);
    }

    [Fact]
    public async Task No_new_version_means_no_download()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        _web.Requests.Clear();
        Assert.Equal(ToolHealth.Current, await manager.UpdateYtDlpAsync());
        Assert.DoesNotContain(_web.Requests, u => u.ToString() == ToolVersions.YtDlpExe);
        Assert.Null(manager.PreviousYtDlpPath);
    }

    [Fact]
    public async Task Rollback_restores_the_prior_version_and_refuses_that_update_again()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));
        await manager.UpdateYtDlpAsync();

        await manager.RollbackAsync();

        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));
        Assert.False(manager.UpdateUnproven);

        // v2 is still the latest online, but it just failed real work.
        Assert.Equal(ToolHealth.UpdatePending, await manager.UpdateYtDlpAsync());
        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));

        // A newer release gets its chance.
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v3"));
        Assert.Equal(ToolHealth.Current, await manager.UpdateYtDlpAsync());
        Assert.Equal("yt-dlp v3", File.ReadAllText(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task A_locked_file_during_an_update_leaves_the_working_version_in_place()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));
        await manager.UpdateYtDlpAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v3"));

        ToolHealth health;
        using (File.Open(manager.PreviousYtDlpPath!, FileMode.Open, FileAccess.Read, FileShare.None))
            health = await manager.EnsureAsync();

        Assert.Equal(ToolHealth.UpdatePending, health);
        Assert.Equal("yt-dlp v2", File.ReadAllText(Installed("yt-dlp.exe")));
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.EndsWith(".new.exe"));
    }

    [Fact]
    public async Task Rollback_with_nothing_to_roll_back_to_is_harmless()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        await manager.RollbackAsync();
        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task Update_state_survives_a_restart()
    {
        var first = Manager();
        await first.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));
        await first.UpdateYtDlpAsync();

        Assert.True(Manager().UpdateUnproven);
        Assert.NotNull(Manager().PreviousYtDlpPath);

        first.MarkUpdateProven();
        Assert.False(Manager().UpdateUnproven);
    }

    [Fact]
    public async Task No_network_returns_Missing_without_throwing()
    {
        _web.Offline = true;
        Assert.Equal(ToolHealth.Missing, await Manager().EnsureAsync());
    }

    [Fact]
    public async Task No_network_with_tools_installed_is_not_a_problem()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        _web.Offline = true;
        Assert.Equal(ToolHealth.Current, await manager.EnsureAsync());
    }

    [Fact]
    public async Task Missing_deno_degrades_but_still_works()
    {
        _web.Files.Remove(ToolVersions.DenoZip);
        var manager = Manager();
        Assert.Equal(ToolHealth.Degraded, await manager.EnsureAsync());
        Assert.Null(manager.Paths.JsRuntime);
        Assert.Equal(Installed("ffmpeg.exe"), manager.Paths.Ffmpeg);
    }

    [Fact]
    public async Task Missing_ffmpeg_is_missing()
    {
        _web.Files.Remove(FfmpegSource.Zip);
        Assert.Equal(ToolHealth.Missing, await Manager().EnsureAsync());
    }

    [Fact]
    public async Task A_failed_download_can_be_retried_by_ensuring_again()
    {
        _web.Offline = true;
        var manager = Manager();
        await manager.EnsureAsync();
        _web.Offline = false;
        Assert.Equal(ToolHealth.Current, await manager.EnsureAsync());
    }

    [Fact]
    public async Task A_broken_installed_tool_is_replaced()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        File.Delete(Installed("deno.exe"));
        Assert.Equal(ToolHealth.Current, await manager.EnsureAsync());
        Assert.True(File.Exists(Installed("deno.exe")));
    }

    [Fact]
    public async Task Updates_off_makes_no_requests()
    {
        Assert.Equal(ToolHealth.Missing, await Manager(updates: false).EnsureAsync());
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task Updates_off_leaves_installed_tools_alone()
    {
        await Manager().EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));
        _web.Requests.Clear();
        var manager = Manager(updates: false);

        Assert.Equal(ToolHealth.Current, await manager.EnsureAsync());
        Assert.Equal(ToolHealth.Current, await manager.UpdateYtDlpAsync());

        Assert.Empty(_web.Requests);
        Assert.Equal("yt-dlp v1", File.ReadAllText(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task Ensuring_also_updates_yt_dlp_because_sites_break_often()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        PublishYtDlp(Encoding.UTF8.GetBytes("yt-dlp v2"));
        await manager.EnsureAsync();
        Assert.Equal("yt-dlp v2", File.ReadAllText(Installed("yt-dlp.exe")));
    }

    [Fact]
    public async Task Update_never_contacts_media_sites()
    {
        var manager = Manager();
        await manager.EnsureAsync();
        await manager.UpdateYtDlpAsync();
        Assert.NotEmpty(_web.Requests);
        Assert.All(_web.Requests, u => Assert.Contains(u.Host, ToolVersions.UpdateHosts));
        Assert.DoesNotContain("youtube.com", ToolVersions.UpdateHosts);
    }

    [Fact]
    public async Task Progress_names_each_tool()
    {
        var seen = new List<string>();
        await Manager().EnsureAsync(new SyncProgress(s => seen.Add(s.Tool)));
        Assert.Contains("yt-dlp", seen);
        Assert.Contains("Deno", seen);
        Assert.Contains("FFmpeg", seen);
    }

    sealed class SyncProgress(Action<ToolStatus> report) : IProgress<ToolStatus>
    {
        public void Report(ToolStatus value) => report(value);
    }

    [Fact]
    public async Task No_partial_download_is_left_behind()
    {
        PublishYtDlp(_ytDlp, claimedHash: new string('a', 64));
        await Manager().EnsureAsync();
        Assert.DoesNotContain(Directory.GetFiles(_root), f => f.EndsWith(".tmp") || f.EndsWith(".new"));
    }

    [Fact]
    public async Task User_cancel_is_not_swallowed()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manager().EnsureAsync(null, cancel.Token));
    }
}

public class ChecksumTests
{
    const string Hash = "cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf";

    [Fact]
    public void Reads_a_sums_file_by_name() =>
        Assert.Equal(Hash, Checksums.Find($"{new string('1', 64)}  yt-dlp\n{Hash}  yt-dlp.exe\n", "yt-dlp.exe"));

    [Fact]
    public void Does_not_confuse_a_name_with_a_longer_one() =>
        Assert.Null(Checksums.Find($"{Hash}  yt-dlp.exe.zip\n", "yt-dlp.exe"));

    [Fact]
    public void Reads_the_starred_binary_form() =>
        Assert.Equal(Hash, Checksums.Find($"{Hash} *yt-dlp.exe\n", "yt-dlp.exe"));

    [Fact]
    public void Reads_deno_style_powershell_output_as_the_real_file_has_it() =>
        Assert.Equal(Hash, Checksums.FindOnly($"Algorithm : SHA256\r\nHash      : {Hash.ToUpperInvariant()}\r\nPath      : C:\\a\\deno.zip\r\n"));

    [Theory]
    [InlineData("")]
    [InlineData("not a checksum")]
    [InlineData("abc123  yt-dlp.exe")]
    public void Garbage_is_no_checksum(string text) => Assert.Null(Checksums.Find(text, "yt-dlp.exe"));
}

sealed class FakeVersions(string current, string? previous, bool unproven) : IYtDlpVersions
{
    public int RollBacks, Proven;
    public string YtDlpPath { get; } = current;
    public string? PreviousYtDlpPath { get; } = previous;
    public bool UpdateUnproven { get; } = unproven;
    public Task RollbackAsync() { RollBacks++; return Task.CompletedTask; }
    public void MarkUpdateProven() => Proven++;
}

sealed class ScriptedProcess(Func<string, (int Exit, string Output)> script) : IProcessRunner
{
    public readonly List<string> Paths = new();

    public Task<int> RunAsync(string path, IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancel)
    {
        Paths.Add(path);
        var (exit, output) = script(path);
        if (output.Length > 0) onLine(output);
        return Task.FromResult(exit);
    }
}

public sealed class UpdateRetryTests : IDisposable
{
    const string SiteBroke = "ERROR: [youtube] abc: nsig extraction failed";
    const string NetworkDown = "ERROR: Unable to download webpage: The read operation timed out";

    readonly string _root = Directory.CreateTempSubdirectory("stx-retry").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    static Job Link() => new(1, 1, JobKind.Link, JobState.Running, 0, null, "original", DateTimeOffset.UnixEpoch);

    YtDlpRunner Runner(IProcessRunner process, IYtDlpVersions versions) =>
        new(process, versions, _ => new DownloadRequest("https://x/y", _root, DownloadMode.Best, null), new CaptureVault(), _root);

    [Fact]
    public async Task Post_update_site_failure_retries_on_previous_then_rolls_back()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        var process = new ScriptedProcess(path => path == "new.exe" ? (1, SiteBroke) : (0, ""));

        var outcome = await Runner(process, versions).RunAsync(Link(), default);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(["new.exe", "old.exe"], process.Paths);
        Assert.Equal(1, versions.RollBacks);
    }

    [Fact]
    public async Task Rolling_back_is_announced_because_the_user_is_told()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        var runner = Runner(new ScriptedProcess(path => path == "new.exe" ? (1, SiteBroke) : (0, "")), versions);
        int told = 0;
        runner.RolledBack += (_, _) => told++;
        await runner.RunAsync(Link(), default);
        Assert.Equal(1, told);
    }

    [Fact]
    public async Task Network_failure_after_update_does_not_roll_back()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        var process = new ScriptedProcess(_ => (1, NetworkDown));

        var outcome = await Runner(process, versions).RunAsync(Link(), default);

        Assert.Equal(ErrorKind.Network, outcome.Error);
        Assert.Equal(["new.exe"], process.Paths);
        Assert.Equal(0, versions.RollBacks);
    }

    [Theory]
    [InlineData("ERROR: unable to write data: [Errno 28] No space left on device", ErrorKind.OutputFailure)]
    [InlineData("ERROR: [generic] x: This video is DRM protected", ErrorKind.DrmDetected)]
    public async Task Failures_the_tool_version_cannot_cause_do_not_retry(string output, ErrorKind kind)
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        var process = new ScriptedProcess(_ => (1, output));
        Assert.Equal(kind, (await Runner(process, versions).RunAsync(Link(), default)).Error);
        Assert.Single(process.Paths);
    }

    [Fact]
    public async Task When_the_previous_version_also_fails_nothing_rolls_back()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        var process = new ScriptedProcess(_ => (1, SiteBroke));

        var outcome = await Runner(process, versions).RunAsync(Link(), default);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(2, process.Paths.Count);
        Assert.Equal(0, versions.RollBacks);
    }

    [Fact]
    public async Task A_proven_update_never_retries()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: false);
        var process = new ScriptedProcess(_ => (1, SiteBroke));
        await Runner(process, versions).RunAsync(Link(), default);
        Assert.Single(process.Paths);
    }

    [Fact]
    public async Task With_no_previous_version_there_is_nothing_to_retry_on()
    {
        var versions = new FakeVersions("new.exe", null, unproven: true);
        var process = new ScriptedProcess(_ => (1, SiteBroke));
        await Runner(process, versions).RunAsync(Link(), default);
        Assert.Single(process.Paths);
    }

    [Fact]
    public async Task A_success_proves_the_update()
    {
        var versions = new FakeVersions("new.exe", "old.exe", unproven: true);
        await Runner(new ScriptedProcess(_ => (0, "")), versions).RunAsync(Link(), default);
        Assert.Equal(1, versions.Proven);
        Assert.Equal(0, versions.RollBacks);
    }
}
