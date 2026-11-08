using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.Data;
using Xunit;

namespace Stx.Data.Tests;

public sealed class DataTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-data").FullName;
    string DbPath => Path.Combine(_dir, "stx.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static Job NewJob(JobKind kind = JobKind.Link, JobState state = JobState.Queued) =>
        new(0, 1, kind, state, 0, null, "original", new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Schema_has_no_secret_columns()
    {
        using var db = Db.Open(DbPath);
        var columns = new List<string>();
        foreach (string table in Db.Tables(db))
            columns.AddRange(Db.Columns(db, table));
        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, c => Regex.IsMatch(c, "cookie|header|token|stream_url", RegexOptions.IgnoreCase));
    }

    [Theory]
    [InlineData("media_items", "id,page_url,source_id,title,origin,title_key")]
    [InlineData("media_files", "id,media_id,path,hash,format,size,modified,path_key,listed")]
    [InlineData("jobs", "id,media_id,kind,state,attempts,error_kind,preset,created_at")]
    public void Tables_have_the_planned_columns(string table, string expected)
    {
        using var db = Db.Open(DbPath);
        Assert.Equal(expected.Split(','), Db.Columns(db, table));
    }

    [Theory]
    [InlineData("playlists")]
    [InlineData("playlist_items")]
    [InlineData("audio_facts")]
    public void Planned_tables_exist(string table)
    {
        using var db = Db.Open(DbPath);
        Assert.Contains(table, Db.Tables(db));
    }

    [Fact]
    public void Opening_twice_keeps_the_data_and_does_not_migrate_again()
    {
        long id;
        using (var db = Db.Open(DbPath)) id = new JobRepo(db).Add(NewJob()).Id;
        using var again = Db.Open(DbPath);
        Assert.Equal(Migrations.Latest, Db.Version(again));
        Assert.NotNull(new JobRepo(again).Get(id));
    }

    [Fact]
    public void A_new_folder_is_created_for_the_database()
    {
        string nested = Path.Combine(_dir, "a", "b", "stx.db");
        using var db = Db.Open(nested);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Database_lives_in_local_app_data() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STXDownloader", "stx.db"), Db.DefaultPath());

    [Fact]
    public void Job_round_trips_every_field()
    {
        using var db = Db.Open(DbPath);
        var repo = new JobRepo(db);
        var saved = repo.Add(NewJob(JobKind.Captured, JobState.Waiting) with { Attempts = 2, Error = ErrorKind.Network, Preset = "car-usb" });
        var read = repo.Get(saved.Id)!;
        Assert.Equal(saved, read);
    }

    [Fact]
    public void Job_update_is_saved()
    {
        using var db = Db.Open(DbPath);
        var repo = new JobRepo(db);
        var saved = repo.Add(NewJob());
        repo.Update(saved with { State = JobState.Failed, Error = ErrorKind.DrmDetected, Attempts = 3 });
        var read = repo.Get(saved.Id)!;
        Assert.Equal(JobState.Failed, read.State);
        Assert.Equal(ErrorKind.DrmDetected, read.Error);
        Assert.Equal(3, read.Attempts);
    }

    [Fact]
    public void A_missing_job_is_null()
    {
        using var db = Db.Open(DbPath);
        Assert.Null(new JobRepo(db).Get(404));
    }

    [Fact]
    public void All_returns_jobs_in_order()
    {
        using var db = Db.Open(DbPath);
        var repo = new JobRepo(db);
        var a = repo.Add(NewJob());
        var b = repo.Add(NewJob());
        Assert.Equal([a.Id, b.Id], repo.All().Select(j => j.Id));
    }

    sealed class NoRunner : IJobRunner
    {
        public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) => new TaskCompletionSource<JobOutcome>().Task;
    }

    sealed class StillClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;
    }

    /// <summary>Review Focus 4: app killed mid-job, then opened again on the real database file.</summary>
    [Fact]
    public void A_killed_app_recovers_from_the_real_file_with_nothing_stuck()
    {
        long linkId, capturedId, doneId;
        using (var first = Db.Open(DbPath))
        {
            var repo = new JobRepo(first);
            linkId = repo.Add(NewJob(JobKind.Link, JobState.Running)).Id;
            capturedId = repo.Add(NewJob(JobKind.Captured, JobState.Running)).Id;
            doneId = repo.Add(NewJob(JobKind.Link, JobState.Completed)).Id;
        }

        using var second = Db.Open(DbPath);
        var reopened = new JobRepo(second);
        new QueueEngine(reopened, new NoRunner(), new QueuePolicy { MaxParallel = 0 }, new StillClock(), new CaptureVault()).RecoverAfterRestart();

        Assert.Equal(JobState.Queued, reopened.Get(linkId)!.State);
        Assert.Equal(JobState.NeedsRecapture, reopened.Get(capturedId)!.State);
        Assert.Equal(JobState.Completed, reopened.Get(doneId)!.State);
    }

    [Fact]
    public void A_query_string_with_quotes_cannot_break_the_statement()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        string title = "Robert'); DROP TABLE jobs;--";
        long id = media.AddItem("https://example.com/watch?v=1", "src", title);
        Assert.Equal(title, media.GetItem(id)!.Title);
        Assert.Contains("jobs", Db.Tables(db));
    }

    [Fact]
    public void Ordinary_links_keep_their_query_so_they_still_work()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long id = media.AddItem("https://www.youtube.com/watch?v=abc123&list=PL1", "yt:abc123", "Song");
        Assert.Equal("https://www.youtube.com/watch?v=abc123&list=PL1", media.GetItem(id)!.PageUrl);
    }

    [Theory]
    [InlineData("https://cdn.example/watch?token=SECRET&v=1")]
    [InlineData("https://cdn.example/watch?v=1&sig=SECRET")]
    [InlineData("https://cdn.example/hdntl=exp=1~hmac=SECRET/index.m3u8")]
    [InlineData("https://user:SECRET@cdn.example/watch")]
    public void Tokenized_links_are_never_written(string url)
    {
        using (var db = Db.Open(DbPath))
        {
            new MediaRepo(db).AddItem(url, "src", "Title");
        }
        SqliteConnection.ClearAllPools();
        string everything = string.Concat(Directory.GetFiles(_dir).Select(f => File.ReadAllText(f, System.Text.Encoding.Latin1)));
        Assert.DoesNotContain("SECRET", everything);
    }

    [Fact]
    public void A_files_identity_is_stored_and_read_back_with_its_item()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long item = media.AddItem("https://x/1", "yt:abc", "Song");
        long file = media.AddFile(item, @"D:\Music\Copy.m4a", null, "Original");

        media.SetFileIdentity(file, "abc123", @"D:\Music\Kept.m4a");

        var fact = Assert.Single(media.ExistingFacts());
        Assert.Equal("abc123", fact.Hash);
        Assert.Equal(@"D:\Music\Kept.m4a", fact.Path);
        Assert.Equal(item, fact.MediaId);
        Assert.Equal(@"D:\Music\Kept.m4a", media.FilePath(file));
        Assert.Equal(fact, Dedupe.SameBytes("abc123", @"D:\Music\New.m4a", media.ExistingFacts()));
        Assert.Null(Dedupe.SameBytes("abc123", @"d:\music\KEPT.m4a", media.ExistingFacts()));
    }

    [Fact]
    public void Files_feed_the_duplicate_check()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long item = media.AddItem("https://x/1", "yt:abc", "Song");
        media.AddFile(item, @"D:\Music\Song.m4a", "hash1", "m4a");

        var facts = media.ExistingFacts();

        Assert.Equal(DedupeDecision.Duplicate, Dedupe.Decide("yt:abc", null, "m4a", facts));
        Assert.Equal(DedupeDecision.New, Dedupe.Decide("yt:abc", null, "flac", facts));
    }

    [Fact]
    public void The_library_lists_items_with_their_files_newest_first()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long old = media.AddItem("https://x/1", "a", "Old");
        long fresh = media.AddItem("https://x/2", "b", "New");
        media.AddItem("https://x/3", "c", "Nothing saved yet");
        media.AddFile(old, @"D:\old.mp4", null, "Original");
        media.AddFile(fresh, @"D:\new.mp4", null, "Original");
        media.AddFile(fresh, @"D:\new.flac", null, "Flac");

        var library = media.Rows(LibrarySearch.All, null, 100);

        // One row per file: the newest item's two files in the order they were saved, then the older item's.
        Assert.Equal(["New", "New", "Old"], library.Select(r => r.Title));
        Assert.Equal([@"D:\new.mp4", @"D:\new.flac", @"D:\old.mp4"], library.Select(r => r.Path));
        Assert.Equal(["Original", "Flac"], library[0].SiblingFormats);
    }

    [Fact]
    public void Facts_are_kept_per_file_and_replaced_when_measured_again()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long file = media.AddFile(media.AddItem("https://x/1", "a", "T"), "p", null, "Original");
        Assert.Null(media.LoadFacts(file));

        media.SaveFacts(file, "first");
        media.SaveFacts(file, "second");

        Assert.Equal("second", media.LoadFacts(file));
    }

    [Fact]
    public void A_file_path_can_be_looked_up()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long file = media.AddFile(media.AddItem("https://x/1", "a", "T"), @"D:\s.mp4", null, "Original");
        Assert.Equal(@"D:\s.mp4", media.FilePath(file));
        Assert.Null(media.FilePath(404));
    }

    [Fact]
    public void Conversion_jobs_persist()
    {
        using var db = Db.Open(DbPath);
        var repo = new JobRepo(db);
        var job = repo.Add(NewJob(JobKind.Convert) with { Preset = "convert:Flac:7" });
        Assert.Equal(JobKind.Convert, repo.Get(job.Id)!.Kind);
    }

    [Fact]
    public void Deleting_a_media_item_removes_its_files()
    {
        using var db = Db.Open(DbPath);
        var media = new MediaRepo(db);
        long item = media.AddItem("https://x/1", "yt:abc", "Song");
        media.AddFile(item, "p", "h", "m4a");
        media.DeleteItem(item);
        Assert.Empty(media.ExistingFacts());
    }

    [Fact]
    public void A_remembered_capture_reads_back_and_goes_with_its_item()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        long media = repo.AddItem("https://a.example", null, "Clip");
        Assert.Null(repo.RememberedCapture("KEY"));

        repo.RememberCapture("KEY", media);
        Assert.Equal(media, repo.RememberedCapture("KEY"));

        repo.DeleteItem(media);
        Assert.Null(repo.RememberedCapture("KEY"));
    }
}
