using System.Diagnostics;
using Stx.Core.Library;
using Stx.Core.Queue;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>An in-memory Library with the same rules as MediaRepo.</summary>
sealed class MemoryCollection : ICollectionCatalog
{
    readonly object _gate = new();
    readonly List<IndexedFile> _files = [];
    long _next = 1;
    public int AddCalls;

    public IReadOnlyList<IndexedFile> IndexedFiles() { lock (_gate) return [.. _files]; }

    public IReadOnlyList<string> Paths() { lock (_gate) return [.. _files.Select(f => f.Path).Order(StringComparer.Ordinal)]; }

    public void AddLocal(IReadOnlyList<FoundFile> files, MediaOrigin origin)
    {
        lock (_gate)
        {
            AddCalls++;
            foreach (var f in files) { long id = _next++; _files.Add(new IndexedFile(id, id, f.Path, f.Size, f.Modified, origin)); }
        }
    }

    public void AddDownload(string path) { lock (_gate) { long id = _next++; _files.Add(new IndexedFile(id, id, path, null, null, MediaOrigin.Downloaded)); } }

    public void MoveFile(long fileId, string newPath)
    {
        lock (_gate) { int i = _files.FindIndex(f => f.FileId == fileId); _files[i] = _files[i] with { Path = newPath }; }
    }

    public void UpdateFacts(long fileId, long size, DateTimeOffset modified)
    {
        lock (_gate) { int i = _files.FindIndex(f => f.FileId == fileId); _files[i] = _files[i] with { Size = size, Modified = modified }; }
    }

    public int ForgetWatchedUnder(string folder)
    {
        lock (_gate) return _files.RemoveAll(f => f.Origin == MediaOrigin.Watched && FolderScan.IsUnder(f.Path, folder));
    }
}

public sealed class FolderScanTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stx-scan").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    string Make(string relative, int bytes = 16)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void Finds_media_in_subfolders_and_nothing_else()
    {
        Make("a.mp4");
        Make(Path.Combine("Albums", "Kind of Blue", "01.flac"));
        Make("notes.txt");
        Make("cover.jpg");
        Make("movie.mkv.part");
        var found = FolderScan.Find(_root, _ => false).Select(f => Path.GetRelativePath(_root, f.Path)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal([Path.Combine("Albums", "Kind of Blue", "01.flac"), "a.mp4"], found);
    }

    // The downloads folder sits inside Videos. The queue records downloads itself, and yt-dlp's part files have media names.
    [Fact]
    public void An_ignored_folder_is_skipped()
    {
        Make("home.mp4");
        Make(Path.Combine("STX Downloader", "clip.f137.mp4"));
        string downloads = Path.Combine(_root, "STX Downloader");
        var found = FolderScan.Find(_root, p => FolderScan.IsUnder(p, downloads)).ToList();
        Assert.Equal("home.mp4", Path.GetFileName(Assert.Single(found).Path));
    }

    [Fact]
    public void A_light_index_reads_size_and_date_only()
    {
        string path = Make("a.mp3", 1234);
        var found = Assert.Single(FolderScan.Find(_root, _ => false));
        Assert.Equal(1234, found.Size);
        Assert.Equal(File.GetLastWriteTimeUtc(path), found.Modified.UtcDateTime);
    }

    [Fact]
    public void Nothing_already_listed_is_added_twice_whatever_its_origin()
    {
        var at = DateTimeOffset.UnixEpoch;
        var plan = FolderScan.Plan(_root,
            [new(Path.Combine(_root, "a.mp4"), 1, at), new(Path.Combine(_root, "b.mp4"), 2, at)],
            [new IndexedFile(1, 1, Path.Combine(_root, "a.mp4"), null, null, MediaOrigin.Downloaded)],
            _ => true);
        Assert.Equal("b.mp4", Path.GetFileName(Assert.Single(plan.Adds).Path));
        Assert.Empty(plan.Updates);
    }

    [Fact]
    public void A_file_moved_while_STX_was_closed_keeps_its_row()
    {
        var at = DateTimeOffset.UnixEpoch.AddDays(9);
        string old = Path.Combine(_root, "Old", "clip.mp4"), moved = Path.Combine(_root, "New", "clip renamed.mp4");
        var plan = FolderScan.Plan(_root, [new(moved, 777, at)], [new IndexedFile(5, 5, old, 777, at, MediaOrigin.Watched)], _ => false);
        Assert.Empty(plan.Adds);
        Assert.Equal((5L, moved), Assert.Single(plan.Moves));
    }

    [Fact]
    public void Two_lookalike_files_are_not_guessed_at()
    {
        var at = DateTimeOffset.UnixEpoch;
        var plan = FolderScan.Plan(_root,
            [new(Path.Combine(_root, "x.mp4"), 777, at), new(Path.Combine(_root, "y.mp4"), 777, at)],
            [new IndexedFile(5, 5, Path.Combine(_root, "gone.mp4"), 777, at, MediaOrigin.Watched)],
            _ => false);
        Assert.Empty(plan.Moves);
        Assert.Equal(2, plan.Adds.Count);
    }

    [Fact]
    public void A_changed_size_is_noted()
    {
        var at = DateTimeOffset.UnixEpoch;
        string path = Path.Combine(_root, "a.mkv");
        var plan = FolderScan.Plan(_root, [new(path, 900, at)], [new IndexedFile(3, 3, path, 10, at, MediaOrigin.Watched)], _ => true);
        Assert.Equal((3L, 900L, at), Assert.Single(plan.Updates));
    }

    [Theory]
    [InlineData("Music", "Music", true)]
    [InlineData("Music/a.mp3", "Music", true)]
    [InlineData("Music2/a.mp3", "Music", false)]
    [InlineData("Mus", "Music", false)]
    public void Under_means_inside_the_folder(string path, string folder, bool under) =>
        Assert.Equal(under, FolderScan.IsUnder(Path.Combine(_root, path), Path.Combine(_root, folder)));
}

public sealed class CollectionWatcherTests : IDisposable
{
    static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);
    readonly string _root = Directory.CreateTempSubdirectory("stx-watch").FullName;
    readonly MemoryCollection _catalog = new();

    public void Dispose() => Directory.Delete(_root, true);

    string Make(string relative, int bytes = 16)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    static void WaitFor(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) Assert.Fail("Timed out waiting for " + what);
            Thread.Sleep(25);
        }
    }

    CollectionWatcher Watching(Func<string, bool>? ignore = null)
    {
        var watcher = new CollectionWatcher(_catalog, ignore ?? (_ => false), Settle);
        watcher.Watch([_root]);
        Assert.True(watcher.Scanning.Wait(TimeSpan.FromSeconds(10)), "The first scan didn't finish.");
        return watcher;
    }

    [Fact]
    public void The_first_scan_lists_what_is_there()
    {
        Make("a.mp4");
        Make(Path.Combine("sub", "b.flac"));
        using var watcher = Watching();
        Assert.Equal(2, _catalog.IndexedFiles().Count);
        Assert.All(_catalog.IndexedFiles(), f => Assert.Equal(MediaOrigin.Watched, f.Origin));
    }

    // Done when: a scan of a large folder doesn't block the UI.
    [Fact]
    public void A_big_first_scan_runs_in_the_background_below_normal_priority()
    {
        for (int n = 0; n < 3000; n++) Make(Path.Combine($"Artist {n / 30}", $"Track {n}.mp3"), 1);
        ThreadPriority? priority = null;
        using var watcher = new CollectionWatcher(_catalog, _ => false, Settle);
        watcher.Progress += (_, _) => priority ??= Thread.CurrentThread.Priority;

        var clock = Stopwatch.StartNew();
        watcher.Watch([_root]);
        var returned = clock.Elapsed;

        Assert.True(returned < TimeSpan.FromMilliseconds(250), $"Watch held its caller for {returned}.");
        Assert.True(watcher.Scanning.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(3000, _catalog.IndexedFiles().Count);
        Assert.Equal(ThreadPriority.BelowNormal, priority);
        Assert.True(_catalog.AddCalls >= 6, "Adds should come in batches, so rows appear as the scan goes.");
    }

    [Fact]
    public void Scanning_again_adds_nothing()
    {
        Make("a.mp4");
        using (Watching()) { }
        using var again = Watching();
        Assert.Single(_catalog.IndexedFiles());
    }

    // Done when: adding, renaming and deleting files are reflected correctly.
    [Fact]
    public void A_new_file_appears()
    {
        using var watcher = Watching();
        int changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        Make("arrived.mkv");
        WaitFor(() => _catalog.Paths().Any(p => p.EndsWith("arrived.mkv", StringComparison.Ordinal)), "the new file");
        WaitFor(() => changes > 0, "a Changed event");
    }

    [Fact]
    public void A_renamed_file_keeps_its_row()
    {
        string old = Make("clip.mp4");
        using var watcher = Watching();
        long id = _catalog.IndexedFiles().Single().FileId;
        File.Move(old, Path.Combine(_root, "Holiday.mp4"));
        WaitFor(() => _catalog.Paths().SequenceEqual([Path.Combine(_root, "Holiday.mp4")]), "the rename");
        Assert.Equal(id, _catalog.IndexedFiles().Single().FileId);
    }

    [Fact]
    public void A_renamed_folder_takes_its_files_with_it()
    {
        Make(Path.Combine("Album", "01.flac"));
        Make(Path.Combine("Album", "02.flac"));
        using var watcher = Watching();
        Directory.Move(Path.Combine(_root, "Album"), Path.Combine(_root, "Album (Remastered)"));
        WaitFor(() => _catalog.Paths().All(p => p.Contains("Album (Remastered)", StringComparison.Ordinal)), "the folder rename");
        Assert.Equal(2, _catalog.IndexedFiles().Count);
    }

    [Fact]
    public void A_deleted_file_stays_listed_and_the_library_is_told()
    {
        string path = Make("gone.mp3");
        using var watcher = Watching();
        int changes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref changes);
        File.Delete(path);
        WaitFor(() => changes > 0, "a Changed event");
        Assert.Single(_catalog.IndexedFiles());
    }

    [Fact]
    public void A_burst_of_new_files_is_one_update()
    {
        using var watcher = Watching();
        int before = _catalog.AddCalls;
        for (int n = 0; n < 50; n++) Make($"burst {n}.mp3");
        WaitFor(() => _catalog.IndexedFiles().Count == 50, "all fifty files");
        Assert.True(_catalog.AddCalls - before <= 3, $"{_catalog.AddCalls - before} adds for one burst.");
    }

    [Fact]
    public void Files_STX_tracks_another_way_are_left_alone()
    {
        string downloads = Path.Combine(_root, "STX Downloader");
        using var watcher = Watching(p => FolderScan.IsUnder(p, downloads));
        Make(Path.Combine("STX Downloader", "video.f137.mp4"));
        Make("home.mp4");
        WaitFor(() => _catalog.IndexedFiles().Count == 1, "home.mp4");
        Thread.Sleep(Settle * 3);
        Assert.Equal("home.mp4", Path.GetFileName(_catalog.IndexedFiles().Single().Path));
    }

    [Fact]
    public void Removing_a_folder_forgets_its_watched_rows_and_keeps_downloads()
    {
        Make("a.mp4");
        string saved = Make("saved.mp4");
        _catalog.AddDownload(saved);
        using var watcher = Watching();
        Assert.Equal(2, _catalog.IndexedFiles().Count);
        watcher.Watch([]);
        Assert.Equal(saved, _catalog.IndexedFiles().Single().Path);
        Assert.True(File.Exists(Path.Combine(_root, "a.mp4")), "Removing a watched folder must never delete files.");
    }

    [Fact]
    public void A_folder_that_isnt_there_does_not_stop_the_others()
    {
        Make("a.mp4");
        using var watcher = new CollectionWatcher(_catalog, _ => false, Settle);
        watcher.Watch([Path.Combine(_root, "no such folder"), _root]);
        Assert.True(watcher.Scanning.Wait(TimeSpan.FromSeconds(10)));
        Assert.Single(_catalog.IndexedFiles());
    }
}
