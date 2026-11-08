using Stx.Core.Logging;
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
    // Lock order: _work, then _gate; never the other way round.
    readonly object _gate = new();
    // The catalog's work, one piece at a time: a scan's batches, a set of changes, forgetting a folder. Before the
    // review of 2026-11-07 the first scan and the watcher each decided from their own reading of the index, and both
    // added the same new file; and the timer could start a second set of changes while the first still ran.
    readonly object _work = new();
    // A folder whose watcher couldn't start (an unplugged drive, a share not up yet) is listed with no watcher, and
    // looked for again every so often until it appears.
    readonly Dictionary<string, FileSystemWatcher?> _watchers = new(StringComparer.OrdinalIgnoreCase);
    readonly List<FsEvent> _pending = [];
    readonly Timer _timer;
    readonly Timer _retry;
    readonly TimeSpan _retryMissing;
    readonly CancellationTokenSource _closing = new();
    Task _scanning = Task.CompletedTask;
    bool _disposed;
    int _failures;

    const int MostRetries = 3;

    /// <param name="ignore">Paths STX tracks another way, such as the downloads folder, which the queue records itself.</param>
    /// <param name="settle">How long a folder must be quiet before its changes are applied.</param>
    /// <param name="retryMissing">How often a watched folder that isn't there is looked for again.</param>
    public CollectionWatcher(ICollectionCatalog catalog, Func<string, bool> ignore, TimeSpan? settle = null, TimeSpan? retryMissing = null)
    {
        _catalog = catalog;
        _ignore = ignore;
        _settle = settle ?? TimeSpan.FromMilliseconds(750);
        _retryMissing = retryMissing ?? TimeSpan.FromSeconds(30);
        _timer = new Timer(_ => ApplyPending(), null, Timeout.Infinite, Timeout.Infinite);
        _retry = new Timer(_ => LookForMissing(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Something in the Library changed. Raised off the UI thread.</summary>
    public event EventHandler? Changed;

    public event EventHandler<ScanProgress>? Progress;

    /// <summary>The scan of folders added by the last <see cref="Watch"/>, for tests and for shutting down.</summary>
    public Task Scanning { get { lock (_gate) return _scanning; } }

    /// <summary>Watches exactly these folders. A new one is scanned in the background; this returns at once.
    /// A folder no longer listed has its watched items taken out of the Library. Files on disk are never touched.</summary>
    public void Watch(IReadOnlyList<string> folders)
    {
        var wanted = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<string> added, removed;
        bool missing = false;
        lock (_gate)
        {
            if (_disposed) return;
            removed = _watchers.Keys.Where(f => !wanted.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            added = wanted.Where(f => !_watchers.ContainsKey(f)).ToList();
            foreach (string folder in removed)
            {
                _watchers[folder]?.Dispose();
                _watchers.Remove(folder);
            }
            foreach (string folder in added) missing |= (_watchers[folder] = Start(folder)) is null;
            if (missing) _retry.Change(_retryMissing, _retryMissing);
        }

        bool forgot = false;
        // A folder still watched (say, a parent of the one removed) keeps what's inside it. Under the work lock, so a
        // scan of the folder still running can't add rows back after they're forgotten: it stops at its next batch.
        lock (_work)
            foreach (string folder in removed)
                if (!wanted.Any(w => FolderScan.IsUnder(folder, w))) forgot |= _catalog.ForgetWatchedUnder(folder) > 0;
        if (forgot) Changed?.Invoke(this, EventArgs.Empty);
        if (added.Count > 0) ScanInBackground(added);
    }

    // Scans the folders one after another on a thread of their own, below normal priority, after any scan already
    // going. A folder that fails is written to the error log and the rest still run: before the review of 2026-11-07
    // one failure was handed on to every later scan, so no folder added afterwards was scanned until a restart.
    void ScanInBackground(IReadOnlyList<string> folders)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var previous = _scanning;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scanning = done.Task;
            var thread = new Thread(() =>
            {
                previous.Wait();
                foreach (string folder in folders)
                {
                    if (_closing.IsCancellationRequested) break;
                    try { ScanFolder(folder, _closing.Token); }
                    catch (OperationCanceledException) { break; }
                    catch (Exception e)
                    {
                        ErrorLog.Write("library scan", e);
                        Progress?.Invoke(this, new ScanProgress(folder, 0, 0, true));
                    }
                }
                done.SetResult();
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "STX library scan" };
            thread.Start();
        }
    }

    bool IsWatched(string folder)
    {
        lock (_gate) return !_disposed && _watchers.ContainsKey(folder);
    }

    // A watched folder that wasn't there when it was added (a drive not plugged in at launch, say) is watched and
    // scanned as soon as it appears. Before, it stayed unwatched until STX started again.
    void LookForMissing()
    {
        var found = new List<string>();
        lock (_gate)
        {
            if (_disposed) return;
            foreach (string folder in _watchers.Where(w => w.Value is null).Select(w => w.Key).ToList())
                if (Start(folder) is { } watcher)
                {
                    _watchers[folder] = watcher;
                    found.Add(folder);
                }
            if (_watchers.Values.All(w => w is not null)) _retry.Change(Timeout.Infinite, Timeout.Infinite);
        }
        if (found.Count > 0) ScanInBackground(found);
    }

    void ScanFolder(string folder, CancellationToken cancel)
    {
        int seen = 0, added = 0;
        var found = new List<FoundFile>();
        foreach (var file in FolderScan.Find(folder, _ignore, cancel))
        {
            found.Add(file);
            if (++seen % Batch == 0) Progress?.Invoke(this, new ScanProgress(folder, seen, added, false));
        }

        ScanPlan plan;
        lock (_work)
        {
            if (!IsWatched(folder)) { Progress?.Invoke(this, new ScanProgress(folder, seen, added, true)); return; }
            plan = FolderScan.Plan(folder, found, _catalog.IndexedFiles(), File.Exists);
            foreach (var (id, path) in plan.Moves) _catalog.MoveFile(id, path);
            foreach (var (id, size, modified) in plan.Updates) _catalog.UpdateFacts(id, size, modified);
        }
        var lastChanged = DateTime.UtcNow;
        if (plan.Moves.Count + plan.Updates.Count > 0) Changed?.Invoke(this, EventArgs.Empty);
        for (int start = 0; start < plan.Adds.Count; start += Batch)
        {
            cancel.ThrowIfCancellationRequested();
            var slice = plan.Adds.Skip(start).Take(Batch).ToList();
            // Each batch takes its turn with the watcher's changes. A file the watcher added meanwhile is already
            // indexed, and the catalog doesn't add it again. A folder taken off the list stops here.
            lock (_work)
            {
                if (!IsWatched(folder)) break;
                _catalog.AddLocal(slice, MediaOrigin.Watched);
            }
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
        if (added > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    // A watcher on the folder, or null when the folder isn't there to watch.
    FileSystemWatcher? Start(string folder)
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
            // The folder isn't there (an unplugged drive, say). It stays on the list and is looked for again.
            watcher.Dispose();
            return null;
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
        bool any;
        lock (_work)
        {
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0) return;
                changes = [.. _pending];
                _pending.Clear();
            }
            try
            {
                any = Apply(changes);
                _failures = 0;
            }
            catch (OperationCanceledException)
            {
                return; // Closing.
            }
            catch (Exception e)
            {
                // This runs on a timer thread, where anything thrown ends the app; a busy or locked database
                // (SqliteException) used to do exactly that. The changes are kept and tried again shortly; after a few
                // failures in a row they're let go, and the next launch's scan catches up.
                ErrorLog.Write("library watcher", e);
                if (++_failures <= MostRetries)
                    lock (_gate)
                        if (!_disposed)
                        {
                            _pending.InsertRange(0, changes);
                            _timer.Change(_settle, Timeout.InfiniteTimeSpan);
                        }
                return;
            }
        }
        if (any) Changed?.Invoke(this, EventArgs.Empty);
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
                // Renamed onto a file the Library lists (Explorer's "replace"): one row per path. A download keeps its
                // row, since it has a history; otherwise the row that moved is the one that stays.
                if (byPath.TryGetValue(to, out var displaced) && displaced.FileId != 0 && displaced.FileId != known.FileId)
                {
                    if (displaced.Origin != MediaOrigin.Downloaded) _catalog.ForgetFile(displaced.FileId);
                    else if (known.Origin != MediaOrigin.Downloaded)
                    {
                        _catalog.ForgetFile(known.FileId);
                        any = true;
                        continue;
                    }
                }
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
            foreach (var watcher in _watchers.Values) watcher?.Dispose();
            _watchers.Clear();
            _pending.Clear();
        }
        _closing.Cancel();
        _timer.Dispose();
        _retry.Dispose();
        try { Scanning.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        // The token source isn't disposed: a scan that outlives the wait still reads it on its way out.
    }

    enum FsChange { Created, Changed, Deleted, Renamed, Lost }

    sealed record FsEvent(FsChange Kind, string Path, string? OldPath);
}
