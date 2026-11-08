namespace Stx.Core.Memory;

/// <summary>
/// Gives Windows back the memory a big piece of work left behind. The first scan of a 50,000-file folder leaves about
/// 45 MB of dead objects in the GC's oldest generation and large-object heap, and an idle app allocates nothing, so the
/// GC never runs again to take them back: measured 2026-11-07, the idle app held 48 MB of committed heap with 4 MB of
/// it alive. Once the work has been quiet for <c>quiet</c>, one collection compacts the heap and returns what's free.
/// Work that ends again inside that time waits for it to pass, so a run of jobs costs one collection, not one each.
/// </summary>
public sealed class MemoryReturn : IDisposable
{
    readonly TimeSpan _quiet;
    readonly Action _collect;
    readonly Timer _timer;
    int _disposed;

    /// <param name="collect">What returns the memory; <see cref="Collect"/> in the app, a counter in a test.</param>
    public MemoryReturn(TimeSpan quiet, Action? collect = null)
    {
        _quiet = quiet;
        _collect = collect ?? Collect;
        _timer = new Timer(_ => Fire());
    }

    /// <summary>Big work just ended (a scan, a job): the memory goes back once it's been quiet for a while.</summary>
    public void WorkEnded()
    {
        if (Volatile.Read(ref _disposed) == 0) _timer.Change(_quiet, Timeout.InfiniteTimeSpan);
    }

    void Fire()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _collect(); }
        catch (Exception e) { Logging.ErrorLog.Write("memory return", e); }
    }

    /// <summary>
    /// A full, blocking, compacting collection in the GC's aggressive mode, which also gives the freed memory back to
    /// Windows. It pauses the app for a few tens of milliseconds, once, after work it would otherwise keep paying for.
    /// </summary>
    public static void Collect() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _timer.Dispose();
    }
}
