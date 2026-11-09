using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Stx.Core.Library;
using Stx.Core.Queue;
using Stx.Core.Settings;
using Stx.Data;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>
/// Phase 2: the Library as the whole collection, searched and kept up to date without rebuilding every row. Since the
/// row-storage plan (2026-11-05), only the rows read so far are held: a page at first, more as the list scrolls.
/// </summary>
public class LibraryCollectionTests
{
    readonly LibraryCatalog _catalog = new();
    readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    LibraryViewModel Library(ILibraryCatalog? catalog = null)
    {
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        return new LibraryViewModel(catalog ?? _catalog, engine, new ShellSpy(), _files.Contains, _ => { });
    }

    void Add(string title, string path, MediaOrigin origin = MediaOrigin.Watched, bool onDisk = true)
    {
        long id = _catalog.Stored.Count + 1;
        // The repository lists newest first.
        _catalog.Stored.Insert(0, new LibraryItem(id, title, null, [new LibraryFile(id, path, "Original")], origin));
        if (onDisk) _files.Add(path);
    }

    void AddTracks(int count)
    {
        for (int n = 0; n < count; n++) Add($"Track {n}", $@"D:\Music\t{n}.mp3");
    }

    static List<NotifyCollectionChangedEventArgs> Record(LibraryViewModel library)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        library.Rows.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }

    [Theory]
    [InlineData(MediaOrigin.Watched, "MKV · WATCHED")]
    [InlineData(MediaOrigin.Opened, "MKV · OPENED")]
    [InlineData(MediaOrigin.Downloaded, "MKV · ORIGINAL")]
    public void Each_row_says_where_it_came_from(MediaOrigin origin, string status)
    {
        Add("Film", @"D:\Videos\film.mkv", origin);
        var library = Library();
        library.Refresh();
        Assert.Equal(status, library.Rows.Single().Status);
    }

    [Fact]
    public void A_refresh_that_changes_nothing_touches_no_row()
    {
        AddTracks(100);
        var library = Library();
        library.Refresh();
        var changes = Record(library);
        library.Refresh();
        Assert.Empty(changes);
    }

    [Fact]
    public void A_new_file_is_one_insert_at_the_top()
    {
        AddTracks(100);
        var library = Library();
        library.Refresh();
        var first = library.Rows[0];
        var changes = Record(library);

        Add("Arrived", @"D:\Music\arrived.mp3");
        library.Refresh();

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(0, change.NewStartingIndex);
        Assert.Same(first, library.Rows[1]);
    }

    // Done when: a deleted file is reflected correctly. It stays listed as FILE MISSING (existing behavior).
    [Fact]
    public void A_deleted_file_is_one_row_replaced_with_file_missing()
    {
        Add("A", @"D:\Music\a.mp3");
        Add("B", @"D:\Music\b.mp3");
        var library = Library();
        library.Refresh();
        var changes = Record(library);
        _files.Remove(@"D:\Music\a.mp3");
        library.Refresh();

        Assert.Equal(2, changes.Count);
        Assert.Equal("FILE MISSING", library.Rows.Single(r => r.Title == "A").Status);
        Assert.Equal(2, library.Rows.Count);
    }

    [Fact]
    public async Task Search_looks_in_the_title_the_file_name_and_the_folder()
    {
        Add("Blue in Green", @"D:\Music\Miles Davis\Kind of Blue\03.flac");
        Add("Holiday", @"D:\Videos\2026\IMG_0042.mp4");
        Add("Something", @"D:\Videos\other.mkv");
        var library = Library();
        library.Refresh();

        await library.SearchAsync("green");
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        await library.SearchAsync("img_0042");
        Assert.Equal("Holiday", Assert.Single(library.Rows).Title);
        await library.SearchAsync("MILES");
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        await library.SearchAsync("");
        Assert.Equal(3, library.Rows.Count);
    }

    [Fact]
    public async Task Every_word_of_a_search_has_to_match()
    {
        Add("Blue in Green", @"D:\Music\Miles Davis\03.flac");
        Add("Green Onions", @"D:\Music\Booker T\01.flac");
        var library = Library();
        library.Refresh();
        await library.SearchAsync("green miles");
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        Assert.Equal("1 OF 2", library.CountText);
    }

    [Fact]
    public async Task A_search_with_no_matches_is_not_an_empty_library()
    {
        Add("A", @"D:\Music\a.mp3");
        var library = Library();
        library.Refresh();
        await library.SearchAsync("zzz");
        Assert.Empty(library.Rows);
        Assert.False(library.IsEmpty);
        Assert.Equal("0 OF 1", library.CountText);
    }

    [Fact]
    public async Task New_files_that_match_appear_in_the_search_as_they_arrive()
    {
        Add("Live at the Village", @"D:\Music\live1.flac");
        var library = Library();
        library.Refresh();
        await library.SearchAsync("live");
        Add("Live Again", @"D:\Music\live2.flac");
        Add("Studio", @"D:\Music\studio.flac");
        await library.RefreshAsync();
        Assert.Equal(["Live Again", "Live at the Village"], library.Rows.Select(r => r.Title));
    }

    // Plan 2026-11-05, step 2: at 50,000 rows the idle app held every row. Now it holds a page, and the list reads the
    // next as it scrolls, each in the Library's order, until there are no more.
    [Fact]
    public async Task Only_a_page_is_held_and_the_rest_comes_as_the_list_scrolls()
    {
        AddTracks(1_000);
        var library = Library();
        library.Refresh();
        Assert.Equal(LibraryViewModel.Page, library.Rows.Count);
        Assert.True(library.HasMore);
        Assert.True(library.Rows.HasMoreRows);
        Assert.Equal("1,000 FILES", library.CountText);

        // The list asks for a few; a page comes, picking up just after the last row.
        Assert.Equal(LibraryViewModel.Page, await library.Rows.LoadMoreRowsAsync(30));
        Assert.Equal("Track 899", library.Rows[LibraryViewModel.Page].Title);
        while (library.Rows.HasMoreRows) await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page);
        Assert.Equal(Enumerable.Range(0, 1_000).Reverse().Select(n => $"Track {n}"), library.Rows.Select(r => r.Title));
        Assert.Equal(0, await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page));
    }

    // Rows counted, then forgotten before the list reached them: the next page comes up short, and the list is told
    // there's no more, or WinUI's list would keep asking for rows that aren't there.
    [Fact]
    public async Task When_rows_counted_have_gone_the_list_stops_asking()
    {
        AddTracks(150);
        var library = Library();
        library.Refresh();
        _catalog.Stored.RemoveRange(90, 60);
        Assert.Equal(0, await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page));
        Assert.False(library.Rows.HasMoreRows);
    }

    // Whether each file is still on disk is checked for the rows read, not for every row in the Library.
    [Fact]
    public void Only_the_rows_read_are_checked_on_disk()
    {
        AddTracks(5_000);
        int checks = 0;
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(_catalog, engine, new ShellSpy(), path => { checks++; return _files.Contains(path); }, _ => { });
        library.Refresh();
        Assert.Equal(LibraryViewModel.Page, checks);
    }

    // A refresh keeps the rows scrolled to: a file arriving at the top is one insert, however far down the list goes.
    [Fact]
    public async Task A_refresh_keeps_the_rows_scrolled_to()
    {
        AddTracks(1_000);
        var library = Library();
        library.Refresh();
        await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page);
        await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page);
        var last = library.Rows[^1];
        var changes = Record(library);

        Add("Arrived", @"D:\Music\arrived.mp3");
        await library.RefreshAsync();

        var change = Assert.Single(changes);
        Assert.Equal((NotifyCollectionChangedAction.Add, 0), (change.Action, change.NewStartingIndex));
        Assert.Equal(3 * LibraryViewModel.Page + 1, library.Rows.Count);
        Assert.Same(last, library.Rows[^1]);
    }

    // When every row was on screen, a refresh reads to the end: a copy saved beside the last row's file shows.
    [Fact]
    public void A_file_saved_below_the_last_row_shows_when_every_row_was_on_screen()
    {
        Add("Film", @"D:\Videos\film.mkv");
        var library = Library();
        library.Refresh();
        _catalog.Stored[0] = _catalog.Stored[0] with { Files = [.. _catalog.Stored[0].Files, new LibraryFile(2, @"D:\Videos\film.flac", "Flac")] };
        _files.Add(@"D:\Videos\film.flac");
        library.Refresh();
        Assert.Equal([@"D:\Videos\film.mkv", @"D:\Videos\film.flac"], library.Rows.Select(r => r.Path));
    }

    [Fact]
    public async Task A_big_change_is_one_reset_not_thousands_of_edits()
    {
        AddTracks(5_000);
        var library = Library();
        library.Refresh();
        for (int n = 0; n < 4; n++) await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page);
        var changes = Record(library);
        await library.SearchAsync("track 1234");
        Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changes[0].Action);
        Assert.Equal("Track 1234", Assert.Single(library.Rows).Title);
    }

    // A scan that adds more rows at once than are worth editing in: the list starts again from the top, as long as it was.
    [Fact]
    public async Task A_flood_of_new_files_starts_the_list_again_from_the_top()
    {
        AddTracks(600);
        var library = Library();
        library.Refresh();
        for (int n = 0; n < 4; n++) await library.Rows.LoadMoreRowsAsync(LibraryViewModel.Page);
        var changes = Record(library);
        for (int n = 0; n < 1_000; n++) Add($"New {n}", $@"D:\Music\new{n}.mp3");
        await library.RefreshAsync();
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(changes).Action);
        Assert.Equal(5 * LibraryViewModel.Page, library.Rows.Count);
        Assert.Equal("New 999", library.Rows[0].Title);
        Assert.Equal("1,600 FILES", library.CountText);
    }

    // Typing on while the database answers: the answer for a word already typed past never reaches the screen, and
    // the rows end up answering the last search typed.
    [Fact]
    public async Task The_rows_answer_the_last_search_typed()
    {
        Add("Blue in Green", @"D:\Music\blue.flac");
        Add("Green Onions", @"D:\Music\onions.flac");
        var slow = new HeldLibrary(_catalog);
        var library = Library(slow);
        library.Refresh();
        var shown = new List<string>();
        library.Rows.CollectionChanged += (_, _) => shown.Add(string.Join(", ", library.Rows.Select(r => r.Title)));

        slow.Hold();
        library.Query = "blue";
        await slow.Held;                       // the read for "blue" is under way
        var settled = library.SearchAsync("onions");
        slow.Release();
        await settled;
        Assert.Equal("Green Onions", Assert.Single(library.Rows).Title);
        Assert.Equal("onions", library.Query);
        Assert.DoesNotContain("Blue in Green", shown);
    }

    // PREV and NEXT walk the Library from the database, as the search narrows it, past the rows read so far and past
    // files gone from disk.
    [Fact]
    public async Task Play_order_walks_the_search_past_the_rows_read_and_past_missing_files()
    {
        for (int n = 0; n < 300; n++) Add($"Song {n}", $@"D:\Music\{n:D3}.flac", onDisk: n != 98);
        Add("Talk", @"D:\Podcasts\talk.mp3");
        var library = Library();
        library.Refresh();
        await library.SearchAsync("song");
        Assert.Equal(LibraryViewModel.Page, library.Rows.Count);

        var order = library.PlayOrder();
        Assert.Equal(new PlayItem(@"D:\Music\199.flac", "Song 199"), await order.AfterAsync(library.Rows[^1].Path));
        Assert.Equal(new PlayItem(@"D:\Music\097.flac", "Song 97"), await order.AfterAsync(@"D:\Music\099.flac"));
        Assert.Equal(new PlayItem(@"D:\Music\099.flac", "Song 99"), await order.BeforeAsync(@"D:\Music\097.flac"));
        Assert.Null(await order.AfterAsync(@"D:\Music\000.flac"));
        Assert.Null(await order.BeforeAsync(@"D:\Music\299.flac"));
        Assert.Null(await order.AfterAsync(@"D:\Podcasts\nowhere.mp3"));
    }

    [Fact]
    public async Task Finding_a_path_gives_the_row_on_screen_when_it_is_showing()
    {
        Add("A", @"D:\Music\a.mp3");
        Add("B", @"D:\Music\b.mp3");
        var library = Library();
        library.Refresh();
        Assert.Same(library.Rows.Single(r => r.Title == "A"), await library.FindAsync(@"d:\MUSIC\A.MP3"));
        await library.SearchAsync("b");
        Assert.Equal("A", (await library.FindAsync(@"D:\Music\a.mp3"))!.Title);
        Assert.Null(await library.FindAsync(@"D:\Music\c.mp3"));
    }

    [Fact]
    public void The_scan_readout_names_the_folder_and_clears_when_done()
    {
        var library = Library();
        library.ShowScan(new ScanProgress(Path.Combine("D:", "Music"), 1234, 1000, false));
        Assert.Equal("SCANNING MUSIC · 1,234 FOUND", library.ScanText);
        library.ShowScan(new ScanProgress(Path.Combine("D:", "Music"), 2000, 2000, true));
        Assert.Equal("", library.ScanText);
    }

    /// <summary>A catalog whose next read waits until it's let go: a slow disk under the database.</summary>
    sealed class HeldLibrary(ILibraryCatalog inner) : ILibraryCatalog
    {
        TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim? _gate;
        public Task Held => _held.Task;
        public void Hold() { _held = new(TaskCreationOptions.RunContinuationsAsynchronously); _gate = new ManualResetEventSlim(); }
        public void Release() => _gate?.Set();

        void Wait()
        {
            if (_gate is not { } gate) return;
            _held.TrySetResult();
            gate.Wait(TimeSpan.FromSeconds(10));
            _gate = null;
        }

        public int Count(LibrarySearch search) { Wait(); return inner.Count(search); }
        public IReadOnlyList<LibraryEntry> Rows(LibrarySearch search, LibraryKey? after, int limit) => inner.Rows(search, after, limit);
        public IReadOnlyList<LibraryEntry> RowsBefore(LibrarySearch search, LibraryKey before, int limit) => inner.RowsBefore(search, before, limit);
        public LibraryEntry? Row(string path) => inner.Row(path);
        public IReadOnlyList<ShrinkPair> ShrinkCopies() => inner.ShrinkCopies();
    }
}

/// <summary>
/// Done when (plan 2026-11-05): search still narrows as you type at 50,000 rows. The view-model on the real database,
/// as the app runs it: each keystroke is a search answered off the UI thread, the count and the first page read and
/// checked on disk.
/// </summary>
public sealed class LibraryAtScaleTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-scale").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task Typing_a_search_on_fifty_thousand_rows_keeps_up()
    {
        using var db = Db.Open(Path.Combine(_dir, "stx.db"));
        var repo = new MediaRepo(db);
        var files = Enumerable.Range(0, 50_000)
            .Select(n => new FoundFile($@"D:\Music\Artist {n % 997}\Album {n % 61}\{n:D5} Song.flac", 1, DateTimeOffset.UnixEpoch)).ToList();
        for (int start = 0; start < files.Count; start += 500) repo.AddLocal(files.GetRange(start, 500), MediaOrigin.Watched);
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        var library = new LibraryViewModel(repo, engine, new ShellSpy(), _ => true, _ => { });
        await library.RefreshAsync();
        Assert.Equal("50,000 FILES", library.CountText);

        // Each keystroke is timed on its own, and the typical one is held to the budget: an average let one pause (the
        // first search's JIT, a collection, a busy CI runner) fail ten keystrokes that kept up (2026-11-09: 160 ms).
        var keystrokes = new List<TimeSpan>();
        string typed = "";
        foreach (char key in "artist 99")
        {
            typed += key;
            var clock = Stopwatch.StartNew();
            await library.SearchAsync(typed);
            keystrokes.Add(clock.Elapsed);
        }
        var clear = Stopwatch.StartNew();
        await library.SearchAsync("");
        keystrokes.Add(clear.Elapsed);
        var typical = keystrokes.Order().ElementAt(keystrokes.Count / 2);

        Assert.Equal(LibraryViewModel.Page, library.Rows.Count);
        Assert.Equal("50,000 FILES", library.CountText);
        Assert.True(typical < TimeSpan.FromMilliseconds(150),
            $"A typical keystroke took {typical.TotalMilliseconds:F0} ms; all of them: {string.Join(", ", keystrokes.Select(k => $"{k.TotalMilliseconds:F0}"))} ms.");
    }
}

public class WatchedFolderSettingsTests
{
    static SettingsViewModel Settings(string file) => new(new SettingsStore(file), new FakeStartup());

    [Fact]
    public void Videos_and_music_are_watched_until_changed()
    {
        string file = Path.Combine(Path.GetTempPath(), "stx-watched-" + Guid.NewGuid() + ".json");
        try
        {
            Assert.Equal(AppSettings.DefaultWatched, Settings(file).WatchedFolders);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Folders_can_be_added_and_removed_and_it_lasts()
    {
        string file = Path.Combine(Path.GetTempPath(), "stx-watched-" + Guid.NewGuid() + ".json");
        try
        {
            var settings = Settings(file);
            AppSettings? told = null;
            settings.Changed += (_, s) => told = s;
            settings.AddWatchedFolder(@"E:\Films");
            Assert.Contains(@"E:\Films", told!.Watched);
            settings.AddWatchedFolder(@"e:\films");
            Assert.Single(settings.WatchedFolders, f => f.Equals(@"E:\Films", StringComparison.OrdinalIgnoreCase));

            foreach (string folder in AppSettings.DefaultWatched) settings.RemoveWatchedFolder(folder);
            Assert.Equal([@"E:\Films"], Settings(file).WatchedFolders);

            settings.RemoveWatchedFolder(@"E:\Films");
            Assert.Empty(Settings(file).WatchedFolders);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void A_hand_edited_list_with_blanks_and_repeats_is_cleaned()
    {
        var clean = new AppSettings { WatchedFolders = ["", @"D:\A", @"d:\a", "  "] }.Sanitized();
        Assert.Equal([@"D:\A"], clean.Watched);
    }
}
