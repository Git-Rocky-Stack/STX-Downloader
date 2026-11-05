using Stx.Core.Queue;

namespace Stx.Core.Library;

/// <summary>How far a watched folder's scan has got, for the Library plate.</summary>
public sealed record ScanProgress(string Folder, int Seen, int Added, bool Done);

/// <summary>
/// Keeps the Library in step with the watched folders. A folder is scanned once in the background, below normal
/// priority, then watched: new files appear, renamed and moved files keep their rows, and deleted files stay listed
/// as FILE MISSING. Changes are gathered and applied together once the folder has been quiet for a moment, so
/// copying a thousand files in is one update, not a thousand.
/// </summary>
public sealed class CollectionWatcher : IDisposable
{
    const int Batch = 500;
    static readonly TimeSpan ChangedEvery = TimeSpan.FromSeconds(1);

    readonly ICollectionCatalog _catalog;
    readonly Func<string, bool> _ignore;
    readonly TimeSpan _settle;
    readonly object _gate = new();
    readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly List<FsEvent> _pending = [];
    readonly Timer _timer;
    readonly CancellationTokenSource _closing = new();
    Task _scanning = Task.CompletedTask;
    bool _disposed;

    /// <param name="ignore">Paths STX tracks another way, such as the downloads folder, which the queue records itself.</param>
    /// <param name="settle">How long a folder must be quiet before its changes are applied.</param>
    public CollectionWatcher(ICollectionCatalog catalog, Func<string, bool> ignore, TimeSpan? settle = null)
    {
        _catalog = catalog;
        _ignore = ignore;
        _settle = settle ?? TimeSpan.FromMilliseconds(750);
        _timer = new Timer(_ => ApplyPending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Something in the Library changed. Raised off the UI thread.</summary>
    public event EventHandler? Changed;

    public event EventHandler<ScanProgress>? Progress;

    /// <summary>The scan of folders added by the last <see cref="Watch"/>, for tests and for shutting down.</summary>
    public Task Scanning { get { lock (_gate) return _scanning; } }

    public IReadOnlyList<string> Folders { get { lock (_gate) return [.. _watchers.Keys]; } }

    /// <summary>Watches exactly these folders. A new one is scanned in the background; this returns at once.
    /// A folder no longer listed has its watched items taken out of the Library. Files on disk are never touched.</summary>
    public void Watch(IReadOnlyList<string> folders)
    {
        var wanted = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<string> added, removed;
        lock (_gate)
        {
            if (_disposed) return;
            removed = _watchers.Keys.Where(f => !wanted.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            added = wanted.Where(f => !_watchers.ContainsKey(f)).ToList();
            foreach (string folder in removed)
            {
                _watchers[folder].Dispose();
                _watchers.Remove(folder);
            }
            foreach (string folder in added) _watchers[folder] = Start(folder);
        }

        bool forgot = false;
        // A folder still watched (say, a parent of the one removed) keeps what's inside it.
        foreach (string folder in removed)
            if (!wanted.Any(w => FolderScan.IsUnder(folder, w))) forgot |= _catalog.ForgetWatchedUnder(folder) > 0;
        if (forgot) Changed?.Invoke(this, EventArgs.Empty);
        if (added.Count == 0) return;

        lock (_gate)
        {
            var previous = _scanning;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scanning = done.Task;
            // Its own thread below normal priority, so a first scan of a big collection never competes with the console.
            var thread = new Thread(() =>
            {
                try
                {
                    previous.Wait();
                    foreach (string folder in added) ScanFolder(folder, _closing.Token);
                    done.SetResult();
                }
                catch (OperationCanceledException) { done.SetResult(); }
                catch (AggregateException e) when (e.InnerException is OperationCanceledException) { done.SetResult(); }
                catch (Exception e) { done.SetException(e); }
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "STX library scan" };
            thread.Start();
        }
    }

    /// <summary>Scans a folder again from scratch, as after a watcher lost track of events.</summary>
    public void Rescan(string folder) => ScanFolder(folder, _closing.Token);

    void ScanFolder(string folder, CancellationToken cancel)
    {
        int seen = 0, added = 0;
        var found = new List<FoundFile>();
        foreach (var file in FolderScan.Find(folder, _ignore, cancel))
        {
            found.Add(file);
            if (++seen % Batch == 0) Progress?.Invoke(this, new ScanProgress(folder, seen, added, false));
        }

        var plan = FolderScan.Plan(folder, found, _catalog.IndexedFiles(), File.Exists);
        foreach (var (id, path) in plan.Moves) _catalog.MoveFile(id, path);
        foreach (var (id, size, modified) in plan.Updates) _catalog.UpdateFacts(id, size, modified);
        var lastChanged = DateTime.UtcNow;
        if (plan.Moves.Count + plan.Updates.Count > 0) Changed?.Invoke(this, EventArgs.Empty);
        for (int start = 0; start < plan.Adds.Count; start += Batch)
        {
            cancel.ThrowIfCancellationRequested();
            var slice = plan.Adds.Skip(start).Take(Batch).ToList();
            _catalog.AddLocal(slice, MediaOrigin.Watched);
            added += slice.Count;
            Progress?.Invoke(this, new ScanProgress(folder, seen, added, false));
            // Rows appear as the scan goes, but the Library isn't asked to redraw more than once a second.
            if (DateTime.UtcNow - lastChanged >= ChangedEvery)
            {
                lastChanged = DateTime.UtcNow;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        Progress?.Invoke(this, new ScanProgress(folder, seen, added, true));
        if (plan.Adds.Count > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    FileSystemWatcher Start(string folder)
    {
        var watcher = new FileSystemWatcher
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            InternalBufferSize = 64 * 1024,
        };
        try
        {
            watcher.Path = folder;
        }
        catch (ArgumentException)
        {
            // The folder isn't there (an unplugged drive, say). It's kept in the list and watched when added again.
            return watcher;
        }
        watcher.Created += (_, e) => Queue(new FsEvent(FsChange.Created, e.FullPath, null));
        watcher.Changed += (_, e) => Queue(new FsEvent(FsChange.Changed, e.FullPath, null));
        watcher.Deleted += (_, e) => Queue(new FsEvent(FsChange.Deleted, e.FullPath, null));
        watcher.Renamed += (_, e) => Queue(new FsEvent(FsChange.Renamed, e.FullPath, e.OldFullPath));
        // Too many changes at once and Windows drops some, so the folder is read again in full.
        watcher.Error += (_, _) => Queue(new FsEvent(FsChange.Lost, folder, null));
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    void Queue(FsEvent change)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending.Add(change);
            _timer.Change(_settle, Timeout.InfiniteTimeSpan);
        }
    }

    void ApplyPending()
    {
        List<FsEvent> changes;
        lock (_gate)
        {
            if (_disposed || _pending.Count == 0) return;
            changes = [.. _pending];
            _pending.Clear();
        }

        try
        {
            if (Apply(changes)) Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The disk or database was busy. The next change, or the next launch's scan, catches up.
        }
    }

    bool Apply(IReadOnlyList<FsEvent> changes)
    {
        bool any = false;
        foreach (var lost in changes.Where(c => c.Kind == FsChange.Lost).Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ScanFolder(lost, _closing.Token);
            any = true;
        }

        var indexed = _catalog.IndexedFiles();
        var byPath = new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in indexed) byPath.TryAdd(file.Path, file);

        // Renames first, in order, so a file renamed twice ends up where it stopped.
        foreach (var rename in changes.Where(c => c.Kind == FsChange.Renamed))
        {
            string from = rename.OldPath!, to = rename.Path;
            if (Directory.Exists(to))
            {
                // A folder was renamed or moved in: its files follow it.
                foreach (var file in byPath.Values.Where(f => FolderScan.IsUnder(f.Path, from)).ToList())
                {
                    string moved = Path.Join(to, file.Path[from.Length..].TrimStart('\\', '/'));
                    _catalog.MoveFile(file.FileId, moved);
                    byPath.Remove(file.Path);
                    byPath[moved] = file with { Path = moved };
                    any = true;
                }
                continue;
            }
            if (byPath.Remove(from, out var known))
            {
                if (!MediaTypes.IsMedia(to) || _ignore(to)) { any = true; continue; }
                _catalog.MoveFile(known.FileId, to);
                byPath[to] = known with { Path = to };
                any = true;
            }
            else if (MediaTypes.IsMedia(to) && !_ignore(to) && !byPath.ContainsKey(to) && Describe(to) is { } added)
            {
                _catalog.AddLocal([added], MediaOrigin.Watched);
                byPath[to] = new IndexedFile(0, 0, to, added.Size, added.Modified, MediaOrigin.Watched);
                any = true;
            }
        }

        var touched = changes.Where(c => c.Kind is FsChange.Created or FsChange.Changed).Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase);
        var fresh = new List<FoundFile>();
        foreach (string path in touched)
        {
            if (_ignore(path)) continue;
            if (Directory.Exists(path))
            {
                // A whole folder arrived; whatever inside isn't listed yet is added.
                fresh.AddRange(FolderScan.Find(path, _ignore, _closing.Token).Where(f => !byPath.ContainsKey(f.Path)));
                continue;
            }
            if (!MediaTypes.IsMedia(path) || Describe(path) is not { } file) continue;
            if (byPath.TryGetValue(path, out var known))
            {
                if (known.FileId != 0 && known.Origin != MediaOrigin.Downloaded && (known.Size != file.Size || known.Modified != file.Modified))
                {
                    _catalog.UpdateFacts(known.FileId, file.Size, file.Modified);
                    any = true;
                }
            }
            else if (!fresh.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                fresh.Add(file);
            }
        }
        if (fresh.Count > 0)
        {
            _catalog.AddLocal(fresh, MediaOrigin.Watched);
            any = true;
        }

        // A deletion changes nothing stored: the row stays and shows FILE MISSING. The Library still has to look again.
        return any || changes.Any(c => c.Kind == FsChange.Deleted && byPath.ContainsKey(c.Path));
    }

    static FoundFile? Describe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FoundFile(info.FullName, info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _pending.Clear();
        }
        _closing.Cancel();
        _timer.Dispose();
        try { Scanning.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        // The token source isn't disposed: a scan that outlives the wait still reads it on its way out.
    }

    enum FsChange { Created, Changed, Deleted, Renamed, Lost }

    sealed record FsEvent(FsChange Kind, string Path, string? OldPath);
}
