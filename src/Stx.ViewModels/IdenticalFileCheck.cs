using System.Security.Cryptography;
using Stx.Core.Queue;

namespace Stx.ViewModels;

/// <summary>
/// The same bytes can arrive from two links, which nothing can tell until the file exists. This records each new file's
/// hash, and when those bytes are already in the library it keeps that file and removes the new copy. Only a file the
/// job itself just wrote is ever removed.
/// </summary>
public sealed class IdenticalFileCheck(Func<IReadOnlyList<MediaFact>> existing, Action<long, string, string> record, Func<string, bool> fileExists, Action<string> delete)
{
    public async Task<SavedMedia> SettleAsync(SavedMedia media, CancellationToken cancel = default)
    {
        string hash;
        try
        {
            hash = await HashOf(media.Path, cancel).ConfigureAwait(false);
            await LearnOlderFiles(media.Path, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // It can't be read right now. It stays in the library with no hash, as it would have been anyway.
            return media;
        }

        var same = Dedupe.SameBytes(hash, media.Path, existing().Where(f => f.Path is { } path && fileExists(path)));
        if (same?.Path is not { } kept || media.WasAlreadyThere)
        {
            record(media.FileId, hash, media.Path);
            return media;
        }

        // Row first: a conversion waiting on this file must never look it up and find a path that's gone.
        record(media.FileId, hash, kept);
        try
        {
            delete(media.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            record(media.FileId, hash, media.Path);
            return media;
        }
        return media with { Path = kept };
    }

    // Files saved before hashes were kept have none. Only one of the same length could match, so only those are read.
    async Task LearnOlderFiles(string path, CancellationToken cancel)
    {
        long length = new FileInfo(path).Length;
        foreach (var older in existing().Where(f => f.Hash is null && f.FileId != 0 && f.Path is { } p && !string.Equals(p, path, StringComparison.OrdinalIgnoreCase) && fileExists(p)).ToList())
        {
            try
            {
                if (new FileInfo(older.Path!).Length == length) record(older.FileId, await HashOf(older.Path!, cancel).ConfigureAwait(false), older.Path!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // That one can't be read right now, so it can't be matched this time.
            }
        }
    }

    /// <summary>The file's sha256, in lower-case hex.</summary>
    public static Task<string> HashOfAsync(string path, CancellationToken cancel) => HashOf(path, cancel);

    static async Task<string> HashOf(string path, CancellationToken cancel)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false)).ToLowerInvariant();
    }
}
