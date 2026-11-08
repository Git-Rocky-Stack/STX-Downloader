using Microsoft.Data.Sqlite;
using Stx.Core.Library;
using Stx.Core.Queue;
using Stx.Data;
using Xunit;

namespace Stx.Data.Tests;

/// <summary>Phase 2: the Library as the whole collection, as the database keeps it.</summary>
public sealed class CollectionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-collection").FullName;
    string DbPath => Path.Combine(_dir, "stx.db");
    static readonly string Root = OperatingSystem.IsWindows() ? @"D:\" : "/d";
    static string P(params string[] parts) => Path.Join([Root, .. parts]);
    static readonly DateTimeOffset When = new(2026, 11, 5, 9, 30, 0, TimeSpan.Zero);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public void Watched_files_are_added_with_their_light_index_and_origin()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Music", "Blue in Green.flac"), 31_000_000, When)], MediaOrigin.Watched);

        var item = Assert.Single(All(repo));
        Assert.Equal("Blue in Green", item.Title);
        Assert.Equal(MediaOrigin.Watched, item.Origin);
        Assert.Null(item.PageUrl);
        var indexed = Assert.Single(repo.IndexedFiles());
        Assert.Equal(31_000_000, indexed.Size);
        Assert.Equal(When, indexed.Modified);
    }

    [Fact]
    public void Downloads_say_downloaded()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddFile(repo.AddItem("https://example.com/v", "v1", "Saved"), P("Videos", "STX Downloader", "Saved.mp4"), null, "Original");
        Assert.Equal(MediaOrigin.Downloaded, Assert.Single(All(repo)).Origin);
    }

    // Before this step, a row with no link and no source id could only have been opened from File Explorer.
    [Fact]
    public void Older_libraries_mark_files_opened_from_explorer_as_opened()
    {
        using (var old = new SqliteConnection($"Data Source={DbPath}"))
        {
            old.Open();
            using var command = old.CreateCommand();
            command.CommandText =
                "CREATE TABLE media_items (id INTEGER PRIMARY KEY, page_url TEXT, source_id TEXT, title TEXT);" +
                "CREATE TABLE media_files (id INTEGER PRIMARY KEY, media_id INTEGER NOT NULL REFERENCES media_items(id) ON DELETE CASCADE, path TEXT NOT NULL, hash TEXT, format TEXT NOT NULL);" +
                "CREATE TABLE jobs (id INTEGER PRIMARY KEY, media_id INTEGER NOT NULL, kind TEXT NOT NULL, state TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, error_kind TEXT, preset TEXT NOT NULL, created_at TEXT NOT NULL);" +
                "CREATE TABLE playlists (id INTEGER PRIMARY KEY, page_url TEXT, title TEXT);" +
                "CREATE TABLE playlist_items (playlist_id INTEGER NOT NULL, media_id INTEGER NOT NULL, position INTEGER NOT NULL, PRIMARY KEY (playlist_id, media_id));" +
                "CREATE TABLE audio_facts (id INTEGER PRIMARY KEY, media_file_id INTEGER NOT NULL, facts_json TEXT NOT NULL);" +
                "CREATE TABLE capture_keys (key_hash TEXT PRIMARY KEY, media_id INTEGER NOT NULL);" +
                "INSERT INTO media_items VALUES (1, 'https://example.com/a', 'a', 'Downloaded one');" +
                "INSERT INTO media_items VALUES (2, NULL, NULL, 'Opened one');" +
                "INSERT INTO media_files VALUES (1, 1, 'D:\\a.mp4', NULL, 'Original');" +
                "INSERT INTO media_files VALUES (2, 2, 'D:\\b.mkv', NULL, 'Original');" +
                "PRAGMA user_version = 2;";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var db = Db.Open(DbPath);
        var origins = All(new MediaRepo(db)).ToDictionary(i => i.Title!, i => i.Origin);
        Assert.Equal(MediaOrigin.Downloaded, origins["Downloaded one"]);
        Assert.Equal(MediaOrigin.Opened, origins["Opened one"]);
    }

    [Fact]
    public void A_renamed_watched_file_keeps_its_row_and_takes_its_new_name()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Videos", "clip.mp4"), 10, When)], MediaOrigin.Watched);
        long id = repo.IndexedFiles().Single().FileId;

        repo.MoveFile(id, P("Videos", "Holiday 2026.mp4"));

        var item = Assert.Single(All(repo));
        Assert.Equal("Holiday 2026", item.Title);
        Assert.Equal(P("Videos", "Holiday 2026.mp4"), item.Path);
        Assert.Equal(id, item.FileId);
    }

    [Fact]
    public void A_moved_download_keeps_the_title_it_was_saved_with()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        long file = repo.AddFile(repo.AddItem("https://example.com/v", "v1", "The Real Title"), P("Videos", "x.mp4"), null, "Original");
        repo.MoveFile(file, P("Videos", "renamed.mp4"));
        Assert.Equal("The Real Title", All(repo).Single().Title);
    }

    [Fact]
    public void Forgetting_a_folder_removes_only_its_watched_rows()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Music", "a.mp3"), 1, When), new FoundFile(P("Music", "Live", "b.mp3"), 2, When), new FoundFile(P("Music2", "c.mp3"), 3, When)], MediaOrigin.Watched);
        repo.AddLocal([new FoundFile(P("Music", "opened.mp3"), 4, When)], MediaOrigin.Opened);
        repo.AddFile(repo.AddItem("https://example.com/v", "v1", "Saved"), P("Music", "saved.mp3"), null, "Original");

        Assert.Equal(2, repo.ForgetWatchedUnder(P("Music", "")));

        var left = All(repo).Select(i => i.Path).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { P("Music", "opened.mp3"), P("Music", "saved.mp3"), P("Music2", "c.mp3") }.Order(StringComparer.Ordinal), left);
    }

    [Fact]
    public void Facts_update_in_place()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Videos", "growing.mkv"), 10, When)], MediaOrigin.Watched);
        long id = repo.IndexedFiles().Single().FileId;
        repo.UpdateFacts(id, 5_000, When.AddMinutes(3));
        var file = repo.IndexedFiles().Single();
        Assert.Equal(5_000, file.Size);
        Assert.Equal(When.AddMinutes(3), file.Modified);
    }

    // A first scan of a big collection is written in batches, each one transaction.
    [Fact]
    public void Twenty_thousand_files_go_in_quickly()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        var files = Enumerable.Range(0, 20_000).Select(n => new FoundFile(P("Music", $"Artist {n / 20}", $"Track {n}.flac"), n, When)).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int start = 0; start < files.Count; start += 500) repo.AddLocal(files.Skip(start).Take(500).ToList(), MediaOrigin.Watched);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), $"Took {clock.Elapsed}.");
        Assert.Equal(20_000, All(repo).Count);
    }

    // Review 2026-11-07: the first scan and the watcher could each find a file new to them and both add it.
    [Fact]
    public void A_file_already_indexed_isnt_added_again_whatever_its_case()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Music", "Song.flac"), 10, When)], MediaOrigin.Watched);
        repo.AddLocal([new FoundFile(P("music", "SONG.FLAC"), 10, When), new FoundFile(P("Music", "Other.flac"), 10, When)], MediaOrigin.Watched);
        repo.AddLocal([new FoundFile(P("Music", "Song.flac"), 10, When)], MediaOrigin.Opened);
        Assert.Equal(2, repo.IndexedFiles().Count);
    }

    [Fact]
    public void A_batch_that_names_a_file_twice_adds_it_once()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(P("Music", "Song.flac"), 10, When), new FoundFile(P("Music", "song.flac"), 10, When)], MediaOrigin.Watched);
        Assert.Single(repo.IndexedFiles());
    }

    // The duplicates already made are merged by step 4: the later row goes, what hung off it moves to the first.
    [Fact]
    public void Duplicate_rows_left_by_the_race_are_merged_and_nothing_else_is_lost()
    {
        using (var db = new SqliteConnection($"Data Source={DbPath}"))
        {
            db.Open();
            Migrations.Apply(db, 3);
            Exec(db,
                // A watched file indexed twice; a shrink copy and a job hang off the second row.
                "INSERT INTO media_items (id, title, origin) VALUES (10, 'Song', 'watched'), (11, 'Song', 'watched');" +
                "INSERT INTO media_files (id, media_id, path, format) VALUES (100, 10, 'D:\\Music\\Song.flac', 'Original'), (101, 11, 'd:\\music\\SONG.flac', 'Original'), (102, 11, 'D:\\Music\\Song.opus', 'Shrink Opus');" +
                "INSERT INTO jobs (media_id, kind, state, preset, created_at) VALUES (11, 'Convert', 'Queued', 'x', '2026-11-07');" +
                // A download and a watched row for the same path: the download is never the one removed.
                "INSERT INTO media_items (id, page_url, source_id, title, origin) VALUES (20, 'https://example.com/v', 'v', 'Saved', 'downloaded');" +
                "INSERT INTO media_items (id, title, origin) VALUES (21, 'Saved', 'watched');" +
                "INSERT INTO media_files (id, media_id, path, format) VALUES (200, 20, 'D:\\Saved.mp4', 'Original'), (201, 21, 'D:\\Saved.mp4', 'Original');" +
                // A file that isn't duplicated stays as it is.
                "INSERT INTO media_items (id, title, origin) VALUES (30, 'Alone', 'watched');" +
                "INSERT INTO media_files (id, media_id, path, format) VALUES (300, 30, 'D:\\Alone.flac', 'Original');");
        }
        SqliteConnection.ClearAllPools();

        using var again = Db.Open(DbPath);
        Assert.Equal(Migrations.Latest, Db.Version(again));
        Assert.Equal(["100:10", "102:10", "200:20", "300:30"], Rows(again, "SELECT id || ':' || media_id FROM media_files ORDER BY id;"));
        Assert.Equal(["10", "20", "30"], Rows(again, "SELECT id FROM media_items ORDER BY id;"));
        Assert.Equal(["10"], Rows(again, "SELECT media_id FROM jobs;"));
    }

    // Review 2026-11-07: one job row this version can't read (a state a newer STX wrote) made every read of the queue throw.
    [Fact]
    public void A_job_this_version_cant_read_is_left_out_and_the_rest_still_load()
    {
        using var db = Db.Open(DbPath);
        var jobs = new JobRepo(db);
        var good = jobs.Add(new Job(0, 1, JobKind.Link, Stx.Core.Jobs.JobState.Queued, 0, null, "original", When));
        Exec(db, "INSERT INTO jobs (media_id, kind, state, preset, created_at) VALUES (2, 'Link', 'Teleporting', 'original', '2026-11-07T00:00:00Z');" +
                 "INSERT INTO jobs (media_id, kind, state, error_kind, preset, created_at) VALUES (3, 'Link', 'Failed', 'SolarFlare', 'original', '2026-11-07T00:00:00Z');");
        var all = jobs.All();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, j => j.Id == good.Id);
        Assert.Equal(Stx.Core.Errors.ErrorKind.ToolFailure, all.Single(j => j.MediaId == 3).Error);
    }

    static IReadOnlyList<LibraryEntry> All(MediaRepo repo) => repo.Rows(LibrarySearch.All, null, int.MaxValue);

    static void Exec(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static List<string> Rows(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        return rows;
    }
}
