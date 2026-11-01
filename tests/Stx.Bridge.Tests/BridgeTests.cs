using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Stx.Bridge;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Data;
using Xunit;

namespace Stx.Bridge.Tests;

/// <summary>Holds every job open until the test finishes it.</summary>
sealed class HeldRunner : IJobRunner
{
    readonly Dictionary<long, TaskCompletionSource<JobOutcome>> _open = new();
    public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
    {
        lock (_open)
        {
            var open = new TaskCompletionSource<JobOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _open[job.Id] = open;
            return open.Task;
        }
    }
    public void Finish(long id, JobOutcome outcome)
    {
        lock (_open) _open[id].SetResult(outcome);
    }
}

sealed class FixedClock : IClock
{
    public DateTimeOffset Now => new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
}

sealed class Rig : IDisposable
{
    public readonly string Path_ = Path.Combine(Path.GetTempPath(), $"stx-bridge-{Guid.NewGuid():N}.db");
    public readonly SqliteConnection Db_;
    public readonly MediaRepo Catalog;
    public readonly JobRepo Jobs;
    public readonly CaptureVault Vault = new();
    public readonly HeldRunner Runner = new();
    public readonly QueueEngine Engine;
    public readonly CaptureHandler Handler;
    public bool Ready = true;

    public Rig(int maxParallel = 3)
    {
        Db_ = Db.Open(Path_);
        Catalog = new MediaRepo(Db_);
        Jobs = new JobRepo(Db_);
        Engine = new QueueEngine(Jobs, Runner, new QueuePolicy { MaxParallel = maxParallel }, new FixedClock(), Vault);
        Handler = new CaptureHandler(Catalog, Engine, () => Ready);
    }

    public void Dispose()
    {
        Db_.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(Path_ + suffix);
    }
}

public class BridgeTests
{
    const string Page = "https://www.chevrolet.com/performance/corvette/experience";
    const string Stream = "https://manifest.prod.boltdns.net/manifest/v1/hls/v4/clear/1/2/10s/master.m3u8?fastly_token=SECRETTOKEN";

    static string Capture(string title = "Walkaround", string stream = Stream, string page = Page, bool drm = false, string kind = "hls", string? cookies = "SECRETCOOKIE") =>
        JsonSerializer.Serialize(new
        {
            Kind = kind, PageUrl = page, Title = title, StreamUrl = stream, DurationSec = 413, Width = 1920, Height = 1080,
            Headers = new Dictionary<string, string> { ["Referer"] = page },
            Cookies = cookies, IsDrm = drm,
        });

    static JsonElement Reply(string json) => JsonDocument.Parse(json).RootElement;
    static bool Ok(string json) => Reply(json).GetProperty("ok").GetBoolean();

    [Fact]
    public void Framing_round_trips_4_byte_length()
    {
        using var stream = new MemoryStream();
        NativeMessaging.Write(stream, "{\"a\":\"é\"}");
        byte[] bytes = stream.ToArray();
        Assert.Equal(Encoding.UTF8.GetByteCount("{\"a\":\"é\"}"), BitConverter.ToInt32(bytes, 0));
        stream.Position = 0;
        Assert.Equal("{\"a\":\"é\"}", NativeMessaging.Read(stream));
    }

    [Fact]
    public void Read_at_the_end_of_the_stream_gives_null()
    {
        using var stream = new MemoryStream();
        Assert.Null(NativeMessaging.Read(stream));
    }

    [Fact]
    public void Oversize_message_rejected()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(NativeMessaging.MaxIncoming + 1));
        Assert.Throws<InvalidDataException>(() => NativeMessaging.Read(stream));
    }

    [Fact]
    public void Negative_length_rejected()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(-1));
        Assert.Throws<InvalidDataException>(() => NativeMessaging.Read(stream));
    }

    [Fact]
    public void Truncated_message_rejected()
    {
        using var stream = new MemoryStream([10, 0, 0, 0, (byte)'{']);
        Assert.Throws<InvalidDataException>(() => NativeMessaging.Read(stream));
    }

    [Fact]
    public void Reply_over_chromes_1_MB_limit_is_refused()
    {
        using var stream = new MemoryStream();
        Assert.Throws<InvalidDataException>(() => NativeMessaging.Write(stream, new string('x', NativeMessaging.MaxOutgoing + 1)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Kind\":5}")]
    [InlineData("{\"Kind\":\"hls\"}")]
    public void Malformed_json_is_rejected_without_crash(string json)
    {
        using var rig = new Rig();
        Assert.False(Ok(rig.Handler.Handle(json)));
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("--exec calc.exe")]
    [InlineData("javascript:alert(1)")]
    public void A_stream_that_is_not_a_web_link_makes_no_job(string stream)
    {
        using var rig = new Rig();
        Assert.False(Ok(rig.Handler.Handle(Capture(stream: stream))));
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Fact]
    public void Drm_message_creates_no_job_and_reports_DrmDetected()
    {
        using var rig = new Rig();
        string reply = rig.Handler.Handle(Capture(drm: true));
        Assert.False(Ok(reply));
        Assert.Equal(nameof(ErrorKind.DrmDetected), Reply(reply).GetProperty("error").GetString());
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Fact]
    public void Capture_is_refused_with_a_reason_until_the_tools_are_ready()
    {
        using var rig = new Rig { Ready = false };
        string reply = rig.Handler.Handle(Capture());
        Assert.False(Ok(reply));
        Assert.Equal("NotReady", Reply(reply).GetProperty("error").GetString());
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Fact]
    public void Capture_enqueues_at_front()
    {
        using var rig = new Rig(maxParallel: 1);
        long media = rig.Catalog.AddItem("https://a.example", null, "Running");
        rig.Engine.Enqueue(media, JobKind.Link, "Original");
        var waiting = rig.Engine.Enqueue(media, JobKind.Link, "Original");
        Assert.True(Ok(rig.Handler.Handle(Capture())));

        var captured = rig.Engine.AllJobs().Single(j => j.Kind == JobKind.Captured);
        Assert.Equal(JobState.Running, captured.State);
        Assert.Equal(JobState.Queued, rig.Engine.AllJobs().Single(j => j.Id == waiting.Id).State);
    }

    [Fact]
    public void Capture_stores_page_and_title_and_gives_the_vault_its_secrets()
    {
        using var rig = new Rig();
        rig.Handler.Handle(Capture());
        var job = rig.Engine.AllJobs().Single();
        var item = rig.Catalog.GetItem(job.MediaId)!;
        Assert.Equal((Page, "Walkaround"), (item.PageUrl, item.Title));
        var secrets = rig.Vault.Get(job.Id)!;
        Assert.Equal(Stream, secrets.StreamUrl);
        Assert.Equal("SECRETCOOKIE", secrets.Cookies);
        Assert.Equal(Page, secrets.Headers["Referer"]);
    }

    [Fact]
    public void Secrets_kept_in_memory_only()
    {
        using var rig = new Rig();
        rig.Handler.Handle(Capture());
        var job = rig.Engine.AllJobs().Single();
        rig.Runner.Finish(job.Id, JobOutcome.Success);
        SpinWait.SpinUntil(() => rig.Engine.AllJobs().Single().State == JobState.Completed, 5000);

        Assert.Null(rig.Vault.Get(job.Id));
        using (var command = rig.Db_.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            command.ExecuteNonQuery();
        }
        foreach (string suffix in new[] { "", "-wal" })
        {
            string file = rig.Path_ + suffix;
            if (!File.Exists(file)) continue;
            using var read = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var text = new StreamReader(read, Encoding.Latin1);
            string bytes = text.ReadToEnd();
            Assert.DoesNotContain("SECRETTOKEN", bytes);
            Assert.DoesNotContain("SECRETCOOKIE", bytes);
            Assert.DoesNotContain("boltdns", bytes);
        }
    }

    [Fact]
    public void A_message_refuses_to_print_its_secrets()
    {
        var message = JsonSerializer.Deserialize<CaptureMessage>(Capture(), CaptureMessage.Options)!;
        Assert.DoesNotContain("SECRET", message.ToString());
    }

    [Fact]
    public void A_refused_message_does_not_echo_secrets_in_the_reply()
    {
        using var rig = new Rig();
        Assert.DoesNotContain("SECRET", rig.Handler.Handle(Capture(drm: true)));
        Assert.DoesNotContain("SECRET", rig.Handler.Handle("{\"Cookies\":\"SECRETCOOKIE\""));
    }

    [Fact]
    public void Recapture_from_same_page_resumes_job()
    {
        using var rig = new Rig();
        rig.Handler.Handle(Capture());
        var job = rig.Engine.AllJobs().Single();
        rig.Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        SpinWait.SpinUntil(() => rig.Engine.AllJobs().Single().State == JobState.NeedsRecapture, 5000);

        const string fresh = "https://manifest.prod.boltdns.net/manifest/v1/hls/v4/clear/1/2/10s/master.m3u8?fastly_token=NEW";
        Assert.True(Ok(rig.Handler.Handle(Capture(stream: fresh))));

        var again = Assert.Single(rig.Engine.AllJobs());
        Assert.Equal(job.Id, again.Id);
        Assert.Equal(JobState.Running, again.State);
        Assert.Equal(fresh, rig.Vault.Get(job.Id)!.StreamUrl);
    }

    [Fact]
    public void A_different_video_on_the_same_page_does_not_resume_that_job()
    {
        using var rig = new Rig();
        rig.Handler.Handle(Capture());
        var job = rig.Engine.AllJobs().Single();
        rig.Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        SpinWait.SpinUntil(() => rig.Engine.AllJobs().Single().State == JobState.NeedsRecapture, 5000);

        rig.Handler.Handle(Capture(title: "Another video"));
        Assert.Equal(2, rig.Engine.AllJobs().Count);
        Assert.Equal(JobState.NeedsRecapture, rig.Engine.AllJobs().Single(j => j.Id == job.Id).State);
    }

    [Fact]
    public void Batch_cookies_cover_every_item_then_are_discarded()
    {
        using var rig = new Rig();
        string batch = JsonSerializer.Serialize(new
        {
            Kind = "batch", PageUrl = Page, Title = "Page",
            Headers = new Dictionary<string, string> { ["Referer"] = Page },
            Cookies = "SECRETCOOKIE",
            Items = new[]
            {
                new { Kind = "hls", Title = "One", StreamUrl = "https://v.example/1.m3u8?t=a" },
                new { Kind = "hls", Title = "Two", StreamUrl = "https://v.example/2.m3u8?t=b" },
                new { Kind = "file", Title = "Three", StreamUrl = "https://v.example/3.mp4?t=c" },
            },
        });
        Assert.True(Ok(rig.Handler.Handle(batch)));

        var jobs = rig.Engine.AllJobs();
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, j => Assert.Equal("SECRETCOOKIE", rig.Vault.Get(j.Id)!.Cookies));

        foreach (var job in jobs) rig.Runner.Finish(job.Id, JobOutcome.Success);
        SpinWait.SpinUntil(() => rig.Engine.AllJobs().All(j => j.State == JobState.Completed), 5000);
        Assert.All(jobs, j => Assert.Null(rig.Vault.Get(j.Id)));
    }

    [Fact]
    public void An_oversized_batch_makes_no_jobs()
    {
        using var rig = new Rig();
        string batch = JsonSerializer.Serialize(new
        {
            Kind = "batch", PageUrl = Page,
            Items = Enumerable.Range(0, CaptureHandler.MaxBatch + 1).Select(i => new { Kind = "hls", Title = $"{i}", StreamUrl = $"https://v.example/{i}.m3u8" }).ToArray(),
        });
        Assert.False(Ok(rig.Handler.Handle(batch)));
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Fact]
    public void YouTube_without_cookies_is_an_ordinary_link_job()
    {
        using var rig = new Rig();
        string message = JsonSerializer.Serialize(new { Kind = "youtube", PageUrl = "https://lrqa.example/webinar", Title = "Webinar", VideoId = "QBwwj5p25AM" });
        Assert.True(Ok(rig.Handler.Handle(message)));
        var job = rig.Engine.AllJobs().Single();
        Assert.Equal(JobKind.Link, job.Kind);
        var item = rig.Catalog.GetItem(job.MediaId)!;
        Assert.Equal(("https://www.youtube.com/watch?v=QBwwj5p25AM", "QBwwj5p25AM"), (item.PageUrl, item.SourceId));
    }

    [Fact]
    public void YouTube_with_cookies_is_captured_so_the_cookies_stay_in_memory()
    {
        using var rig = new Rig();
        string message = JsonSerializer.Serialize(new { Kind = "youtube", PageUrl = "https://www.youtube.com/watch?v=QBwwj5p25AM", VideoId = "QBwwj5p25AM", Cookies = "SECRETCOOKIE" });
        Assert.True(Ok(rig.Handler.Handle(message)));
        var job = rig.Engine.AllJobs().Single();
        Assert.Equal(JobKind.Captured, job.Kind);
        Assert.Equal("https://www.youtube.com/watch?v=QBwwj5p25AM", rig.Vault.Get(job.Id)!.StreamUrl);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("QBwwj5p25AM&x=--exec")]
    [InlineData("../../../../etc")]
    public void A_bad_YouTube_id_makes_no_job(string id)
    {
        using var rig = new Rig();
        Assert.False(Ok(rig.Handler.Handle(JsonSerializer.Serialize(new { Kind = "youtube", PageUrl = Page, VideoId = id }))));
        Assert.Empty(rig.Engine.AllJobs());
    }

    [Fact]
    public void Only_the_headers_a_download_needs_pass_through()
    {
        using var rig = new Rig();
        string message = JsonSerializer.Serialize(new
        {
            Kind = "hls", PageUrl = Page, StreamUrl = "https://v.example/a.m3u8", Title = "T",
            Headers = new Dictionary<string, string> { ["Referer"] = Page, ["User-Agent"] = "UA", ["Origin"] = "https://www.chevrolet.com", ["X-Evil"] = "1" },
        });
        rig.Handler.Handle(message);
        var headers = rig.Vault.Get(rig.Engine.AllJobs().Single().Id)!.Headers;
        Assert.Equal(["Origin", "Referer", "User-Agent"], headers.Keys.Order());
    }

    static string UniqueName() => "stx-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Pipe_name_includes_the_users_sid()
    {
        Assert.Equal($"stx-downloader-{WindowsIdentity.GetCurrent().User}", PipeServer.NameForCurrentUser());
    }

    [Fact]
    public void Pipe_rejects_other_users()
    {
        using var server = PipeServer.CreatePipe(UniqueName());
        var rules = server.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        var me = WindowsIdentity.GetCurrent().User!;

        var allowed = rules.Where(r => r.AccessControlType == AccessControlType.Allow).Select(r => r.IdentityReference).Distinct().ToList();
        Assert.Equal([me], allowed);
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Deny && r.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid, null)));
        Assert.True(server.GetAccessControl().AreAccessRulesProtected);
    }

    [Fact]
    public async Task Pipe_round_trip_reaches_the_handler_and_returns_its_reply()
    {
        string name = UniqueName();
        using var server = new PipeServer(name, json => "{\"echo\":" + json + "}");
        server.Start();
        string reply = await PipeClient.SendAsync(name, "{\"a\":1}", launch: null, TimeSpan.FromSeconds(5));
        Assert.Equal("{\"echo\":{\"a\":1}}", reply);
    }

    [Fact]
    public async Task Pipe_keeps_serving_after_a_client_sends_garbage()
    {
        string name = UniqueName();
        using var server = new PipeServer(name, _ => "{\"ok\":true}");
        server.Start();
        using (var bad = new NamedPipeClientStream(".", name, PipeDirection.InOut))
        {
            await bad.ConnectAsync(5000);
            await bad.WriteAsync(BitConverter.GetBytes(int.MaxValue));
        }
        Assert.Equal("{\"ok\":true}", await PipeClient.SendAsync(name, "{}", null, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Pipe_keeps_serving_after_the_handler_throws()
    {
        string name = UniqueName();
        int calls = 0;
        using var server = new PipeServer(name, _ => ++calls == 1 ? throw new InvalidOperationException("boom") : "{\"ok\":true}");
        server.Start();
        string first = await PipeClient.SendAsync(name, "{}", null, TimeSpan.FromSeconds(5));
        Assert.False(Ok(first));
        Assert.Equal("{\"ok\":true}", await PipeClient.SendAsync(name, "{}", null, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task App_not_running_is_launched_by_host()
    {
        string name = UniqueName();
        PipeServer? started = null;
        int launches = 0;
        string reply = await PipeClient.SendAsync(name, "{}", () =>
        {
            launches++;
            started = new PipeServer(name, _ => "{\"ok\":true}");
            started.Start();
        }, TimeSpan.FromSeconds(10));
        started?.Dispose();
        Assert.Equal(1, launches);
        Assert.Equal("{\"ok\":true}", reply);
    }

    [Fact]
    public async Task App_already_running_is_not_launched_again()
    {
        string name = UniqueName();
        using var server = new PipeServer(name, _ => "{\"ok\":true}");
        server.Start();
        int launches = 0;
        await PipeClient.SendAsync(name, "{}", () => launches++, TimeSpan.FromSeconds(5));
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task An_app_that_never_answers_gives_a_plain_failure()
    {
        string reply = await PipeClient.SendAsync(UniqueName(), "{}", () => { }, TimeSpan.FromMilliseconds(600));
        Assert.False(Ok(reply));
        Assert.Equal("AppNotAvailable", Reply(reply).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Relay_carries_one_message_from_the_browser_to_the_pipe_and_back()
    {
        string name = UniqueName();
        using var server = new PipeServer(name, _ => "{\"ok\":true,\"jobs\":1}");
        server.Start();
        using var input = new MemoryStream();
        NativeMessaging.Write(input, Capture());
        input.Position = 0;
        using var output = new MemoryStream();
        await NativeHostRelay.RunAsync(input, output, name, launch: null, TimeSpan.FromSeconds(5));
        output.Position = 0;
        Assert.Equal("{\"ok\":true,\"jobs\":1}", NativeMessaging.Read(output));
    }

    [Fact]
    public async Task Relay_answers_a_bad_frame_instead_of_crashing()
    {
        using var input = new MemoryStream(BitConverter.GetBytes(-5));
        using var output = new MemoryStream();
        await NativeHostRelay.RunAsync(input, output, UniqueName(), launch: null, TimeSpan.FromMilliseconds(100));
        output.Position = 0;
        Assert.False(Ok(NativeMessaging.Read(output)!));
    }
}

public class ReviewFixTests
{
    const string Page = "https://www.youtube.com/watch?v=QBwwj5p25AM";

    static string Name() => "stx-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_client_that_goes_silent_does_not_stop_the_next_one()
    {
        string name = Name();
        using var server = new PipeServer(name, _ => "{\"ok\":true}", TimeSpan.FromMilliseconds(200));
        server.Start();
        using var silent = new NamedPipeClientStream(".", name, PipeDirection.InOut);
        await silent.ConnectAsync(5000);
        await Task.Delay(600);
        Assert.Equal("{\"ok\":true}", await PipeClient.SendAsync(name, "{}", null, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_slow_handler_still_gets_its_reply_back()
    {
        string name = Name();
        using var server = new PipeServer(name, _ => { Thread.Sleep(500); return "{\"ok\":true}"; }, TimeSpan.FromMilliseconds(100));
        server.Start();
        Assert.Equal("{\"ok\":true}", await PipeClient.SendAsync(name, "{}", null, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Save_all_never_sends_sign_in_cookies_with_YouTube_items()
    {
        using var rig = new Rig();
        string batch = JsonSerializer.Serialize(new
        {
            Kind = "batch", PageUrl = "https://a.example/page", Cookies = "SECRETCOOKIE",
            Items = new object[]
            {
                new { Kind = "youtube", VideoId = "QBwwj5p25AM" },
                new { Kind = "hls", Title = "One", StreamUrl = "https://v.example/1.m3u8" },
            },
        });
        rig.Handler.Handle(batch);
        var jobs = rig.Engine.AllJobs();
        Assert.Equal(JobKind.Link, jobs.Single(j => rig.Catalog.GetItem(j.MediaId)!.SourceId == "QBwwj5p25AM").Kind);
        Assert.Equal("SECRETCOOKIE", rig.Vault.Get(jobs.Single(j => j.Kind == JobKind.Captured).Id)!.Cookies);
    }

    [Fact]
    public void A_recapture_resumes_even_when_the_page_link_carries_a_token()
    {
        using var rig = new Rig();
        const string page = "https://portal.example/watch?id=1&token=FIRST";
        string Message(string pageUrl) => JsonSerializer.Serialize(new { Kind = "hls", PageUrl = pageUrl, Title = "T", StreamUrl = "https://v.example/a.m3u8" });
        rig.Handler.Handle(Message(page));
        var job = rig.Engine.AllJobs().Single();
        rig.Runner.Finish(job.Id, JobOutcome.Failure(ErrorKind.LinkExpired));
        SpinWait.SpinUntil(() => rig.Engine.AllJobs().Single().State == JobState.NeedsRecapture, 5000);

        rig.Handler.Handle(Message("https://portal.example/watch?id=1&token=SECOND"));
        Assert.Equal(job.Id, Assert.Single(rig.Engine.AllJobs()).Id);
    }
}
