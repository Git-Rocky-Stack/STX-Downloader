using System.Collections.Specialized;
using System.Diagnostics;
using Stx.Core.Library;
using Stx.Core.Queue;
using Stx.Core.Settings;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>Phase 2: the Library as the whole collection, searched and kept up to date without rebuilding every row.</summary>
public class LibraryCollectionTests
{
    readonly LibraryCatalog _catalog = new();
    readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    LibraryViewModel Library()
    {
        var engine = new QueueEngine(new MemoryJobs(), new HandRunner(), new QueuePolicy(), new SystemClock(), new CaptureVault());
        return new LibraryViewModel(_catalog, engine, new ShellSpy(), _files.Contains, _ => { });
    }

    void Add(string title, string path, MediaOrigin origin = MediaOrigin.Watched, bool onDisk = true)
    {
        long id = _catalog.Stored.Count + 1;
        // The repository lists newest first.
        _catalog.Stored.Insert(0, new LibraryItem(id, title, null, [new LibraryFile(id, path, "Original")], origin));
        if (onDisk) _files.Add(path);
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
        for (int n = 0; n < 100; n++) Add($"Track {n}", $@"D:\Music\t{n}.mp3");
        var library = Library();
        library.Refresh();
        var changes = Record(library);
        library.Refresh();
        Assert.Empty(changes);
    }

    [Fact]
    public void A_new_file_is_one_insert_at_the_top()
    {
        for (int n = 0; n < 100; n++) Add($"Track {n}", $@"D:\Music\t{n}.mp3");
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
    public void Search_looks_in_the_title_the_file_name_and_the_folder()
    {
        Add("Blue in Green", @"D:\Music\Miles Davis\Kind of Blue\03.flac");
        Add("Holiday", @"D:\Videos\2026\IMG_0042.mp4");
        Add("Something", @"D:\Videos\other.mkv");
        var library = Library();
        library.Refresh();

        library.Query = "green";
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        library.Query = "img_0042";
        Assert.Equal("Holiday", Assert.Single(library.Rows).Title);
        library.Query = "MILES";
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        library.Query = "";
        Assert.Equal(3, library.Rows.Count);
    }

    [Fact]
    public void Every_word_of_a_search_has_to_match()
    {
        Add("Blue in Green", @"D:\Music\Miles Davis\03.flac");
        Add("Green Onions", @"D:\Music\Booker T\01.flac");
        var library = Library();
        library.Refresh();
        library.Query = "green miles";
        Assert.Equal("Blue in Green", Assert.Single(library.Rows).Title);
        Assert.Equal("1 OF 2", library.CountText);
    }

    [Fact]
    public void A_search_with_no_matches_is_not_an_empty_library()
    {
        Add("A", @"D:\Music\a.mp3");
        var library = Library();
        library.Refresh();
        library.Query = "zzz";
        Assert.Empty(library.Rows);
        Assert.False(library.IsEmpty);
    }

    [Fact]
    public void New_files_that_match_appear_in_the_search_as_they_arrive()
    {
        Add("Live at the Village", @"D:\Music\live1.flac");
        var library = Library();
        library.Refresh();
        library.Query = "live";
        Add("Live Again", @"D:\Music\live2.flac");
        Add("Studio", @"D:\Music\studio.flac");
        library.Refresh();
        Assert.Equal(["Live Again", "Live at the Village"], library.Rows.Select(r => r.Title));
    }

    // Done when: search returns results while typing on a large library.
    [Fact]
    public void Typing_a_search_on_fifty_thousand_rows_keeps_up()
    {
        for (int n = 0; n < 50_000; n++) Add($"Track {n}", $@"D:\Music\Artist {n % 997}\Album {n % 61}\{n:D5} Song.flac");
        var library = Library();
        library.Refresh();

        var clock = Stopwatch.StartNew();
        string typed = "";
        foreach (char key in "artist 99")
        {
            typed += key;
            library.Query = typed;
        }
        library.Query = "";
        var perKey = clock.Elapsed / 10;

        Assert.Equal(50_000, library.Rows.Count);
        Assert.True(perKey < TimeSpan.FromMilliseconds(150), $"A keystroke took {perKey.TotalMilliseconds:F0} ms.");
    }

    [Fact]
    public void A_big_change_is_one_reset_not_thousands_of_edits()
    {
        for (int n = 0; n < 5_000; n++) Add($"Track {n}", $@"D:\Music\{n}.mp3");
        var library = Library();
        library.Refresh();
        var changes = Record(library);
        library.Query = "track 1234";
        Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, changes[0].Action);
        Assert.Equal("Track 1234", Assert.Single(library.Rows).Title);
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
