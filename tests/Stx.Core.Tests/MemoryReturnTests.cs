using System.Runtime.CompilerServices;
using Stx.Core.Memory;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>
/// Plan 2026-11-05, step 5: with the Library's rows out of memory, what was left at 50,000 rows was garbage from the
/// first scan that an idle app never collects. The memory goes back once the work has been quiet for a while.
/// </summary>
public class MemoryReturnTests
{
    static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

    static async Task WaitFor(Func<bool> condition, string what)
    {
        for (int i = 0; i < 300 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    [Fact]
    public async Task The_memory_goes_back_once_the_work_has_been_quiet()
    {
        int collected = 0;
        using var memory = new MemoryReturn(Quiet, () => Interlocked.Increment(ref collected));
        memory.WorkEnded();
        Assert.Equal(0, collected);
        await WaitFor(() => collected == 1, "never collected");
    }

    // A scan's folders, or a run of jobs, ending one after another: one collection after the last, not one each.
    [Fact]
    public async Task Work_ending_again_inside_the_quiet_time_costs_one_collection()
    {
        int collected = 0;
        using var memory = new MemoryReturn(Quiet, () => Interlocked.Increment(ref collected));
        for (int n = 0; n < 5; n++)
        {
            memory.WorkEnded();
            await Task.Delay(30, TestContext.Current.CancellationToken);
        }
        await WaitFor(() => collected == 1, "never collected");
        await Task.Delay(Quiet * 2, TestContext.Current.CancellationToken);
        Assert.Equal(1, collected);
    }

    [Fact]
    public async Task Nothing_is_collected_after_closing()
    {
        int collected = 0;
        var memory = new MemoryReturn(Quiet, () => Interlocked.Increment(ref collected));
        memory.WorkEnded();
        memory.Dispose();
        memory.WorkEnded();
        await Task.Delay(Quiet * 3, TestContext.Current.CancellationToken);
        Assert.Equal(0, collected);
    }

    // A scan's worth of paths, alive through a full collection, which moves them into the oldest generation. Returns the
    // heap's size then. Its own frame, never inlined, so nothing in the caller holds the list: a Debug build keeps a
    // local alive to the end of its method, and the garbage would never become garbage (2026-11-13: failed in Debug).
    [MethodImpl(MethodImplOptions.NoInlining)]
    static long FillTheOldestGeneration()
    {
        var garbage = new List<string>();
        for (int n = 0; n < 200_000; n++) garbage.Add($@"D:\Music\Artist {n % 997}\{n:D6} Song.flac");
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        long before = GC.GetGCMemoryInfo(GCKind.FullBlocking).HeapSizeBytes;
        GC.KeepAlive(garbage);
        return before;
    }

    // The real collection: dead objects in the oldest generation are taken back, and the heap shrinks.
    [Fact]
    public void The_real_collection_takes_back_the_oldest_garbage()
    {
        long before = FillTheOldestGeneration();
        MemoryReturn.Collect();
        var after = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        Assert.True(after.Compacted, "the collection didn't compact");
        Assert.True(after.HeapSizeBytes < before - 10_000_000, $"heap {before:N0} before, {after.HeapSizeBytes:N0} after");
    }
}
