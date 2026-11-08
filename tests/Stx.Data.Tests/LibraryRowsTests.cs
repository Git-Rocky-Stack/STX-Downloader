using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Stx.Core.Library;
using Stx.Core.Queue;
using Stx.Data;
using Xunit;

namespace Stx.Data.Tests;

/// <summary>
/// Library rows out of memory (plan 2026-11-05, step 1): the Library is counted, paged and searched in the database.
/// The rules are <see cref="InMemoryLibrary"/>'s, which is what the Library did before; these hold the queries to them.
/// </summary>
public sealed class LibraryRowsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-rows").FullName;
    string DbPath => Path.Combine(_dir, "stx.db");
    // A path that the system's own path rules read as one: titles come from file names, folders from separators.
    static readonly string Root = OperatingSystem.IsWindows() ? @"D:\" : "/d";
    static string P(params string[] parts) => Path.Join([Root, .. parts]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    /// <summary>The same Library in memory, for the rules' answers. Items are kept newest first, files as saved.</summary>
    sealed class Reference : InMemoryLibrary
    {
        public readonly List<LibraryItem> Stored = [];
        protected override IReadOnlyList<LibraryItem> AllItems() => Stored;
    }

    // Every way into the table: downloads with links, files without titles, one file reached by two links (spelt in
    // another case, accented letters included), a converted file, shrink copies with and without an original,
    // accents, quotes and punctuation.
    static (MediaRepo Repo, Reference Rules) Build(SqliteConnection db, int items, int seed)
    {
        var repo = new MediaRepo(db);
        var rules = new Reference();
        var random = new Random(seed);
        string[] words = ["Blue", "in", "Green", "Café", "del", "Mar", "ÉTÉ", "Miles", "Davis", "\"Live\"", "So-What", "x", "Kind", "of", "日本"];
        string[] folders = [@"D:\Music\Miles Davis", @"D:\Music\Café", @"E:\Films", @"\\nas\share\Videos", @"D:\Music\ÉTÉ 2026"];
        var paths = new List<string>();
        for (int n = 0; n < items; n++)
        {
            string? title = random.Next(10) == 0 ? null : string.Join(' ', Enumerable.Range(0, random.Next(1, 4)).Select(_ => words[random.Next(words.Length)]));
            string? link = random.Next(3) == 0 ? null : $"https://example.com/watch?v={n}";
            long id = repo.AddItem(link, link is null ? null : $"v{n}", title);
            var files = new List<LibraryFile>();
            string folder = folders[random.Next(folders.Length)];
            string original = random.Next(8) == 0 && paths.Count > 0
                ? Flip(paths[random.Next(paths.Count)])      // another link to a file already listed, in other case
                : Path.Combine(folder, $"{n:D4} {title ?? "untitled"}.mkv");
            paths.Add(original);
            files.Add(new LibraryFile(repo.AddFile(id, original, null, "Original"), original, "Original"));
            if (random.Next(4) == 0)
            {
                string copy = Path.ChangeExtension(original, ".shrink.mkv");
                files.Add(new LibraryFile(repo.AddFile(id, copy, null, "Shrink AV1"), copy, "Shrink AV1"));
            }
            if (random.Next(6) == 0)
            {
                string flac = Path.ChangeExtension(original, ".flac");
                files.Add(new LibraryFile(repo.AddFile(id, flac, null, "Flac"), flac, "Flac"));
            }
            rules.Stored.Insert(0, new LibraryItem(id, title, link, files));
        }
        // A shrink copy whose item has no original: listed, but there's nothing to measure it against.
        long lone = repo.AddItem(null, null, "Lone copy");
        string lonePath = @"D:\Music\lone.opus";
        rules.Stored.Insert(0, new LibraryItem(lone, "Lone copy", null, [new LibraryFile(repo.AddFile(lone, lonePath, null, "Shrink Opus"), lonePath, "Shrink Opus")]));
        return (repo, rules);

        static string Flip(string path) => new(path.Select(c => char.IsLower(c) ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c)).ToArray());
    }

    static readonly string[] Searches =
    [
        "", "blue", "BLUE green", "café", "CAFÉ", "été", "ét", "é", "mi", "x", "d:", @"\", @"music\miles", @"\\nas", "\"live\"", "\"",
        "so-what", "日本", "日", "of kind", "zzz", "mkv", "0042", "https", "a b", "untitled", "  blue   in  ",
    ];

    [Fact]
    public void The_database_answers_every_question_as_the_rules_do()
    {
        using var db = Db.Open(DbPath);
        var (repo, rules) = Build(db, 400, seed: 7);

        foreach (string text in Searches)
        {
            var search = LibrarySearch.Parse(text);
            var expected = rules.Rows(search, null, int.MaxValue);
            Assert.True(rules.Count(search) == repo.Count(search), $"count for '{text}'");

            // Walked a page at a time, from each page's last row, the pages meet exactly: nothing lost, nothing twice.
            var walked = new List<LibraryEntry>();
            LibraryKey? after = null;
            while (true)
            {
                var page = repo.Rows(search, after, 7);
                walked.AddRange(page);
                if (page.Count < 7) break;
                after = page[^1].Key;
            }
            AssertSame(expected, walked, $"rows for '{text}'");

            foreach (var at in expected.Where((_, i) => i % 9 == 0))
            {
                AssertSame(rules.RowsBefore(search, at.Key, 5), repo.RowsBefore(search, at.Key, 5), $"before {at.Key} for '{text}'");
                AssertSame(rules.Rows(search, at.Key, 5), repo.Rows(search, at.Key, 5), $"after {at.Key} for '{text}'");
            }
        }

        foreach (var entry in rules.Rows(LibrarySearch.All, null, int.MaxValue).Where((_, i) => i % 5 == 0))
        {
            string other = entry.Path.ToUpperInvariant() == entry.Path ? entry.Path.ToLowerInvariant() : entry.Path.ToUpperInvariant();
            AssertSame([rules.Row(entry.Path)!], [repo.Row(other)!], $"row for {other}");
        }
        Assert.Null(repo.Row(@"Q:\nowhere.mkv"));
        Assert.Equal(rules.ShrinkCopies(), repo.ShrinkCopies());
    }

    [Fact]
    public void The_search_index_follows_every_write()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        static LibrarySearch S(string text) => LibrarySearch.Parse(text);

        repo.AddLocal([new FoundFile(P("Music", "Holiday Snaps.mkv"), 10, DateTimeOffset.UnixEpoch)], MediaOrigin.Watched);
        long file = Assert.Single(repo.Rows(S("snaps"), null, 10)).FileId;

        // A rename: the old name is gone from the index, the new name and its title are found.
        repo.MoveFile(file, P("Music", "Wedding Album.mkv"));
        Assert.Empty(repo.Rows(S("snaps"), null, 10));
        Assert.Equal("Wedding Album", Assert.Single(repo.Rows(S("wedding"), null, 10)).Title);

        // A download's file moved to where its bytes already were.
        long item = repo.AddItem("https://example.com/v", "v1", "Concert");
        long saved = repo.AddFile(item, @"D:\Downloads\concert.mp4", null, "Original");
        repo.SetFileIdentity(saved, "hash", @"E:\Archive\concert-2026.mp4");
        Assert.Equal(saved, Assert.Single(repo.Rows(S("archive"), null, 10)).FileId);
        Assert.Empty(repo.Rows(S("downloads"), null, 10));

        // Forgetting a file, a folder, or a whole item (its files go by cascade) takes them out of the index.
        repo.ForgetFile(file);
        Assert.Empty(repo.Rows(S("wedding"), null, 10));
        repo.AddLocal([new FoundFile(P("Watched", "gone.flac"), 1, DateTimeOffset.UnixEpoch)], MediaOrigin.Watched);
        Assert.Equal(1, repo.ForgetWatchedUnder(P("Watched")));
        Assert.Empty(repo.Rows(S("gone"), null, 10));

        // The item deleted here holds the highest file id, so SQLite gives the next file that same id. If the cascade
        // left its old words in the index, the new file would be found by them.
        long last = repo.AddItem(null, null, "Zebra crossing");
        long lastFile = repo.AddFile(last, @"D:\zebra.mkv", null, "Original");
        repo.DeleteItem(last);
        long next = repo.AddFile(repo.AddItem(null, null, "Quiet field"), @"D:\quiet.mkv", null, "Original");
        Assert.Equal(lastFile, next);
        Assert.Empty(repo.Rows(S("zebra"), null, 10));
        Assert.Equal(next, Assert.Single(repo.Rows(S("quiet"), null, 10)).FileId);
        // A retitled item is found by its new title (a rename retitles a watched file's item).
        Assert.Equal("Quiet field", Assert.Single(repo.Rows(S("FIELD"), null, 10)).Title);
    }

    // Two links can lead to one file, and only the first row saved for it is listed. The flag that says so is kept
    // by triggers; when that row goes, or a rename parts the paths or brings them together, the right row takes over.
    [Fact]
    public void One_file_reached_by_two_links_is_listed_once_whatever_happens_to_either()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        long first = repo.AddFile(repo.AddItem("https://example.com/a", "a", "First link"), @"D:\Café\Song.mkv", null, "Original");
        long secondItem = repo.AddItem("https://example.com/b", "b", "Second link");
        long second = repo.AddFile(secondItem, @"d:\CAFÉ\song.MKV", null, "Original");
        Assert.Equal(first, Assert.Single(repo.Rows(LibrarySearch.All, null, 10)).FileId);
        Assert.Equal(first, repo.Row(@"D:\CAFÉ\SONG.MKV")!.FileId);

        // Renamed apart, both are listed; brought back together, the first saved is listed again.
        repo.MoveFile(second, @"D:\Café\Other.mkv");
        Assert.Equal(2, repo.Count(LibrarySearch.All));
        repo.MoveFile(second, @"D:\CAFÉ\SONG.mkv");
        Assert.Equal(first, Assert.Single(repo.Rows(LibrarySearch.All, null, 10)).FileId);

        // The listed row renamed away: the row left at the path is listed in its place. Back again, it gives way.
        repo.MoveFile(first, @"D:\Café\Moved.mkv");
        Assert.Equal([first, second], repo.Rows(LibrarySearch.All, null, 10).Select(r => r.FileId).Order());
        Assert.Equal(second, repo.Row(@"D:\Café\Song.mkv")!.FileId);
        repo.MoveFile(first, @"D:\Café\Song.mkv");
        Assert.Equal(first, Assert.Single(repo.Rows(LibrarySearch.All, null, 10)).FileId);

        // The listed row's item deleted (its file goes by cascade): the other link's row is listed now.
        repo.DeleteItem(repo.Row(@"D:\Café\Song.mkv")!.MediaId);
        var left = Assert.Single(repo.Rows(LibrarySearch.All, null, 10));
        Assert.Equal((second, "Second link"), (left.FileId, left.Title));
        Assert.Equal(1, repo.Count(LibrarySearch.Parse("second")));
        Assert.Equal(1, Count(db, "SELECT count(*) FROM media_files WHERE listed = 1;"));
    }

    // The listing reads the stored flag; it doesn't look for an earlier row with the same path, row by row. Measured
    // 2026-11-07 (Core Ultra 7 258V): a count took 130 ms that way at 50,000 rows and 880 ms at 200,000, against 20
    // and 85 ms with the flag, once the database outgrew SQLite's page cache. The 50,000-row typing test can't tell
    // the two apart on a fast machine (138 ms a keystroke against a 150 ms budget), so this holds the design: a row
    // whose flag is cleared isn't listed, which only a listing that reads the flag can show.
    [Fact]
    public void The_listing_reads_the_stored_flag_not_a_lookup_per_row()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        repo.AddLocal([new FoundFile(@"D:\a song.flac", 1, DateTimeOffset.UnixEpoch), new FoundFile(@"D:\b song.flac", 1, DateTimeOffset.UnixEpoch),
            new FoundFile(@"D:\c song.flac", 1, DateTimeOffset.UnixEpoch)], MediaOrigin.Watched);
        using (var command = db.CreateCommand())
        {
            command.CommandText = "UPDATE media_files SET listed = 0 WHERE path = 'D:\\b song.flac';";
            command.ExecuteNonQuery();
        }
        Assert.Equal(2, repo.Count(LibrarySearch.All));
        Assert.Equal(2, repo.Count(LibrarySearch.Parse("song")));
        Assert.Equal([@"D:\c song.flac", @"D:\a song.flac"], repo.Rows(LibrarySearch.All, null, 10).Select(r => r.Path));
    }

    // A Library an older STX made is indexed when this one first opens it.
    [Fact]
    public void An_older_library_is_indexed_when_it_opens()
    {
        using (var old = new SqliteConnection($"Data Source={DbPath}"))
        {
            old.Open();
            Migrations.Apply(old, 4);
            using var command = old.CreateCommand();
            // Two downloads of one file, the second spelt in other case, accented letters included.
            command.CommandText =
                "INSERT INTO media_items (id, title, origin) VALUES (1, 'Blue in Green', 'watched'), (2, NULL, 'opened');" +
                "INSERT INTO media_items (id, page_url, source_id, title, origin) VALUES (3, 'https://example.com/a', 'a', 'Été', 'downloaded'), (4, 'https://example.com/b', 'b', 'Été again', 'downloaded');" +
                "INSERT INTO media_files (id, media_id, path, format) VALUES (1, 1, 'D:\\Music\\03.flac', 'Original'), (2, 2, 'D:\\Films\\Holiday.mkv', 'Original');" +
                "INSERT INTO media_files (id, media_id, path, format) VALUES (3, 3, 'D:\\Été\\clip.mp4', 'Original'), (4, 4, 'd:\\ÉTÉ\\CLIP.mp4', 'Original');";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        Assert.Equal(Migrations.Latest, Db.Version(db));
        Assert.Equal(1, Assert.Single(repo.Rows(LibrarySearch.Parse("green"), null, 10)).FileId);
        Assert.Equal(2, Assert.Single(repo.Rows(LibrarySearch.Parse("holiday"), null, 10)).FileId);
        Assert.Equal(3, Assert.Single(repo.Rows(LibrarySearch.Parse("ÉTÉ"), null, 10)).FileId);
        Assert.Equal(3, repo.Count(LibrarySearch.All));
    }

    // Done when (plan): search still narrows as you type at 50,000 rows. Moved here from the view-model's test, which
    // timed a list in memory: each keystroke now asks the database for the count and the first page, as the Library does.
    [Fact]
    public void Typing_a_search_on_fifty_thousand_rows_keeps_up()
    {
        using var db = Db.Open(DbPath);
        var repo = new MediaRepo(db);
        var files = Enumerable.Range(0, 50_000)
            .Select(n => new FoundFile($@"D:\Music\Artist {n % 997}\Album {n % 61}\{n:D5} Song.flac", 1, DateTimeOffset.UnixEpoch)).ToList();
        for (int start = 0; start < files.Count; start += 500) repo.AddLocal(files.GetRange(start, 500), MediaOrigin.Watched);
        Assert.Equal(50_000, repo.Count(LibrarySearch.All));

        foreach (string typing in new[] { "artist 99", "song 4", "9" })
        {
            var clock = Stopwatch.StartNew();
            string typed = "";
            foreach (char key in typing)
            {
                typed += key;
                var search = LibrarySearch.Parse(typed);
                repo.Count(LibrarySearch.All);
                repo.Count(search);
                repo.Rows(search, null, 100);
            }
            var perKey = clock.Elapsed / typing.Length;
            Assert.True(perKey < TimeSpan.FromMilliseconds(150), $"Typing '{typing}': a keystroke took {perKey.TotalMilliseconds:F0} ms.");
        }

        // Scrolling: a page from deep in the Library comes as fast as the first.
        var deep = repo.Rows(LibrarySearch.All, null, 49_000)[^1].Key;
        var timer = Stopwatch.StartNew();
        Assert.Equal(100, repo.Rows(LibrarySearch.All, deep, 100).Count);
        Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(50), $"A page at row 49,000 took {timer.Elapsed.TotalMilliseconds:F0} ms.");
    }

    static void AssertSame(IReadOnlyList<LibraryEntry> expected, IReadOnlyList<LibraryEntry> actual, string what)
    {
        Assert.True(expected.Count == actual.Count, $"{what}: {expected.Count} rows expected, {actual.Count} found");
        for (int i = 0; i < expected.Count; i++)
        {
            var (e, a) = (expected[i], actual[i]);
            Assert.True(e.Key == a.Key && e.Title == a.Title && e.PageUrl == a.PageUrl && e.Origin == a.Origin && e.Path == a.Path
                && e.Format == a.Format && e.OriginalPath == a.OriginalPath && e.SiblingFormats.SequenceEqual(a.SiblingFormats),
                $"{what}: row {i} expected {e.Key} {e.Path}, found {a.Key} {a.Path}");
        }
    }

    static long Count(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }
}
