using System.Data.Common;
using System.Diagnostics;
using Stx.Core.Library;
using Stx.Core.Queue;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>
/// The review of 2026-11-07 on the Library watcher: the first scan and the watcher raced each other into duplicate
/// rows, a database error on the timer thread ended the app, one failed scan stopped every later one, a removed
/// folder's scan carried on adding, and a folder missing at launch was never watched.
/// </summary>
public sealed class WatcherReviewTests : IDisposable
{
    static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);
    readonly string _root = Directory.CreateTempSubdirectory("stx-review").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    string Make(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[16]);
        return path;
    }

    static void WaitFor(Func<bool> condition, string what, int seconds = 10)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(seconds)) Assert.Fail("Timed out waiting for " + what);
            Thread.Sleep(25);
        }
    }

    /// <summary>The in-memory catalog, with a hand on it: it can hold the first scan batch, count reads, and fail.</summary>
    sealed class HeldCatalog : ICollectionCatalog
    {
        readonly MemoryCollection _inner = new();
        public readonly ManualResetEventSlim InFirstBatch = new();
        public readonly ManualResetEventSlim Release = new(true);
        public int Reads;
        public int AddsLeftToFail;
        public int ReadsLeftToFail;
        int _batches;

        public IReadOnlyList<IndexedFile> IndexedFiles()
        {
            Interlocked.Increment(ref Reads);
            if (Interlocked.Decrement(ref ReadsLeftToFail) >= 0) throw new FakeDbException();
            return _inner.IndexedFiles();
        }

        public void AddLocal(IReadOnlyList<FoundFile> files, MediaOrigin origin)
        {
            if (Interlocked.Increment(ref _batches) == 1)
            {
                InFirstBatch.Set();
                Release.Wait(TimeSpan.FromSeconds(20));
            }
            if (Interlocked.Decrement(ref AddsLeftToFail) >= 0) throw new IOException("the disk went away");
            _inner.AddLocal(files, origin);
        }

        public void MoveFile(long fileId, string newPath) => _inner.MoveFile(fileId, newPath);
        public void UpdateFacts(long fileId, long size, DateTimeOffset modified) => _inner.UpdateFacts(fileId, size, modified);
        public int ForgetWatchedUnder(string folder) => _inner.ForgetWatchedUnder(folder);
        public void ForgetFile(long fileId) => _inner.ForgetFile(fileId);
        public IReadOnlyList<string> Paths() => _inner.Paths();
    }

    // What Microsoft.Data.Sqlite throws for a busy or locked database: a DbException, which isn't an IOException.
    sealed class FakeDbException() : DbException("database is locked");

    // C1. The watcher's changes wait while a scan batch is being written, instead of reading the index beside it.
    [Fact]
    public async Task A_change_waits_for_the_scan_batch_being_written()
    {
        for (int i = 0; i < 3; i++) Make($"old {i}.mp4");
        var catalog = new HeldCatalog();
        catalog.Release.Reset();
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle);
        watcher.Watch([_root]);
        Assert.True(catalog.InFirstBatch.Wait(TimeSpan.FromSeconds(10)), "the scan never wrote");
        int readsBefore = catalog.Reads;
        Make("new while scanning.mp4");
        Thread.Sleep(Settle * 6);
        Assert.Equal(readsBefore, catalog.Reads);
        catalog.Release.Set();
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(10));
        WaitFor(() => catalog.Paths().Count == 4, "the new file");
    }

    // C2. A busy database on the timer thread used to escape and end the process. The changes are tried again.
    [Fact]
    public async Task A_locked_database_doesnt_end_the_app_and_the_change_is_kept()
    {
        var catalog = new HeldCatalog();
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle);
        watcher.Watch([_root]);
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(10));
        Interlocked.Exchange(ref catalog.ReadsLeftToFail, 1);
        Make("arrived.mp4");
        WaitFor(() => catalog.Paths().Count == 1, "the change, tried again");
        Assert.True(catalog.ReadsLeftToFail < 0, "the database never failed");
    }

    // C3. One folder whose scan fails doesn't stop the scans that come after it.
    [Fact]
    public async Task A_failed_scan_doesnt_stop_later_ones()
    {
        string first = Path.Combine(_root, "first"), second = Path.Combine(_root, "second");
        Make(Path.Combine("first", "a.mp4"));
        Make(Path.Combine("second", "b.mp4"));
        var catalog = new HeldCatalog { AddsLeftToFail = 1 };
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle);
        watcher.Watch([first]);
        watcher.Watch([first, second]);
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(Path.Combine(second, "b.mp4"), catalog.Paths());
    }

    // C4. Taking a folder off the list while its first scan is going stops the scan adding rows back.
    [Fact]
    public async Task A_folder_removed_mid_scan_isnt_added_back()
    {
        for (int i = 0; i < 1200; i++) Make($"{i:D4}.mp3");
        var catalog = new HeldCatalog();
        catalog.Release.Reset();
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle);
        watcher.Watch([_root]);
        Assert.True(catalog.InFirstBatch.Wait(TimeSpan.FromSeconds(20)), "the scan never wrote");
        var removing = Task.Run(() => watcher.Watch([]));
        Thread.Sleep(200);
        catalog.Release.Set();
        await removing.WaitAsync(TimeSpan.FromSeconds(10));
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Empty(catalog.Paths());
    }

    // C5. A folder that wasn't there at launch (a drive plugged in later) is watched once it appears.
    [Fact]
    public async Task A_folder_that_appears_later_is_watched_then()
    {
        string later = Path.Combine(_root, "plugged in later");
        var catalog = new HeldCatalog();
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle, retryMissing: TimeSpan.FromMilliseconds(200));
        watcher.Watch([later]);
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(10));
        Make(Path.Combine("plugged in later", "there all along.mp4"));
        WaitFor(() => catalog.Paths().Count == 1, "the folder's first scan");
        Make(Path.Combine("plugged in later", "copied in.mp4"));
        WaitFor(() => catalog.Paths().Count == 2, "the folder's watcher");
    }

    // C6. Renaming onto a file the Library lists (Explorer's "replace") leaves one row for the path, not two.
    [Fact]
    public async Task Renaming_onto_a_listed_file_leaves_one_row()
    {
        string a = Make("a.mp4"), b = Make("b.mp4");
        var catalog = new HeldCatalog();
        using var watcher = new CollectionWatcher(catalog, _ => false, Settle);
        watcher.Watch([_root]);
        await watcher.Scanning.WaitAsync(TimeSpan.FromSeconds(10));
        File.Move(a, b, overwrite: true);
        WaitFor(() => catalog.Paths().Count == 1, "one row");
        Thread.Sleep(Settle * 4);
        Assert.Equal([b], catalog.Paths());
    }
}
