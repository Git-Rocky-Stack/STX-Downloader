using Xunit;
using Stx.Core.Logging;

namespace Stx.Core.Tests;

/// <summary>The error log: what background work that failed leaves behind, scrubbed, bounded, and never a new failure.</summary>
[Collection(ErrorLogCollection.Name)]
public sealed class ErrorLogTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-log").FullName;
    readonly string _was = ErrorLog.FilePath;

    public ErrorLogTests() => ErrorLog.FilePath = Path.Combine(_dir, "logs", "errors.log");

    public void Dispose()
    {
        ErrorLog.FilePath = _was;
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void An_exception_is_written_with_where_it_happened()
    {
        ErrorLog.Write("library watcher", new InvalidOperationException("the database was locked"));
        string text = File.ReadAllText(ErrorLog.FilePath);
        Assert.Contains("[library watcher]", text);
        Assert.Contains("the database was locked", text);
        Assert.Contains(nameof(InvalidOperationException), text);
    }

    [Fact]
    public void Tokens_in_links_never_reach_the_disk()
    {
        ErrorLog.Write("bridge", "failed https://example.com/v.m3u8?token=SECRET123&x=1");
        Assert.DoesNotContain("SECRET123", File.ReadAllText(ErrorLog.FilePath));
    }

    [Fact]
    public void The_log_is_kept_under_a_megabyte_by_moving_the_older_half_aside()
    {
        string line = new('x', 4096);
        for (int i = 0; i < 300; i++) ErrorLog.Write("test", line);
        Assert.True(new FileInfo(ErrorLog.FilePath).Length <= 1024 * 1024 + 8192);
        Assert.True(File.Exists(Path.ChangeExtension(ErrorLog.FilePath, ".1.log")));
    }

    [Fact]
    public void A_log_that_cant_be_written_is_skipped_not_thrown()
    {
        // A directory where the file should be: every write fails.
        Directory.CreateDirectory(ErrorLog.FilePath);
        ErrorLog.Write("test", "nowhere to go");
    }

    [Fact]
    public async Task A_background_task_that_throws_is_recorded_and_a_cancellation_isnt()
    {
        await ErrorLog.Observe(Task.FromException(new IOException("disk gone")), "startup");
        await ErrorLog.Observe(Task.FromCanceled(new CancellationToken(true)), "closing");
        string text = File.ReadAllText(ErrorLog.FilePath);
        Assert.Contains("[startup]", text);
        Assert.DoesNotContain("[closing]", text);
    }
}

/// <summary>The log's path is process-wide, so the tests that move it run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ErrorLogCollection
{
    public const string Name = "Error log";
}
