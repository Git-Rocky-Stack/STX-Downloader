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

        var item = Assert.Single(repo.Library());
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
        Assert.Equal(MediaOrigin.Downloaded, Assert.Single(repo.Library()).Origin);
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
        var origins = new MediaRepo(db).Library().ToDictionary(i => i.Title!, i => i.Origin);
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

        var item = Assert.Single(repo.Library());
        Assert.Equal("Holiday 2026", item.Title);
        Assert.Equal(P("Videos", "Holiday 2026.mp4"), item.Files.Single().Path);
        Assert.Equal(id, item.Files.Single().Id);
    }

    [Fact]
    public void A_moved_download_keeps_the_title_it_was_saved_with()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        long file = repo.AddFile(repo.AddItem("https://example.com/v", "v1", "The Real Title"), P("Videos", "x.mp4"), null, "Original");
        repo.MoveFile(file, P("Videos", "renamed.mp4"));
        Assert.Equal("The Real Title", repo.Library().Single().Title);
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

        var left = repo.Library().Select(i => i.Files.Single().Path).Order(StringComparer.Ordinal).ToList();
        Assert.Equal([P("Music", "opened.mp3"), P("Music", "saved.mp3"), P("Music2", "c.mp3")], left);
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
        Assert.Equal(20_000, repo.Library().Count);
    }
}
