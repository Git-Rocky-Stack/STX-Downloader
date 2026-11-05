using LibVLCSharp.Shared;
using Stx.Core.Playback;

namespace Stx.Playback.Tests;

/// <summary>
/// The parity gate: every corpus file plays through STX's engine and its pictures and sound decode. Local, like the
/// screenshot tests: the files are made or fetched by tools/corpus/Make-Corpus.ps1 and never committed.
/// </summary>
public class CorpusTests
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Fact, Trait("Category", "Corpus")]
    public void Every_corpus_file_plays()
    {
        Assert.True(Directory.Exists(Corpus.FilesDir), "Run tools/corpus/Make-Corpus.ps1 first");
        var failures = new List<string>();
        foreach (var entry in Corpus.Manifest())
        {
            string path = Path.Combine(Corpus.FilesDir, entry.File);
            if (!File.Exists(path)) { failures.Add($"{entry.File}: missing, run tools/corpus/Make-Corpus.ps1"); continue; }
            if (entry.Expect == "companion") continue;
            string? problem = entry.Expect switch
            {
                "title" => CheckTitle(path),
                "refused" => CheckRefused(path),
                _ => CheckPlays(path, entry),
            };
            if (problem is not null) failures.Add($"{entry.File} [{string.Join(", ", entry.Features)}]: {problem}");
        }
        Assert.True(failures.Count == 0, $"{failures.Count} corpus file(s) failed:\n" + string.Join("\n", failures));
    }

    // Playing is proven by what decoded, not by the clock: streams without timestamps (raw MPEG video, some Real and
    // VOC files) decode every picture and sample while the position stays at zero.
    static string? CheckPlays(string path, Entry entry)
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        bool reachedPlaying = false;
        engine.Changed += (_, _) => { if (engine.State == PlaybackState.Playing) reachedPlaying = true; };
        engine.Open(path);
        bool ok = Fixture.Within(Limit, () =>
            Volatile.Read(ref reachedPlaying) && engine.State != PlaybackState.Failed
            && (!entry.Video || engine.Decoded.Pictures > 0)
            && (!entry.Audio || engine.Decoded.AudioBlocks > 0)
            && (entry.Expect != "subtitle" || engine.SubtitleTracks.Count > 0));
        if (ok) return null;
        return $"state {engine.State}, reached playing {reachedPlaying}, pictures {engine.Decoded.Pictures}, audio {engine.Decoded.AudioBlocks}, "
            + $"subtitles {engine.SubtitleTracks.Count}, error {engine.Error ?? "none"}, log: {string.Join(" | ", engine.LoggedErrors.TakeLast(3))}";
    }

    // Dirac / VC-2 crashes VLC 3.0.24's engine, so STX must refuse it with its message and decode nothing.
    static string? CheckRefused(string path)
    {
        using var engine = new LibVlcEngine(Fixture.Profile, headless: true);
        engine.Open(path);
        bool ok = Fixture.Within(Limit, () => engine.State == PlaybackState.Failed && engine.Error == LibVlcEngine.DiracRefused);
        return ok && engine.Decoded.Pictures == 0 ? null : $"not refused: state {engine.State}, error {engine.Error ?? "none"}, pictures {engine.Decoded.Pictures}";
    }

    // Tags are read by LibVLC's parser; the corpus files carry the title "STX corpus".
    static string? CheckTitle(string path)
    {
        LibVLCSharp.Shared.Core.Initialize();
        using var vlc = new LibVLC("--no-lua", "--aout=dummy");
        using var media = new Media(vlc, path, FromType.FromPath);
        var status = media.Parse(MediaParseOptions.ParseLocal, 5000).GetAwaiter().GetResult();
        string? title = media.Meta(MetadataType.Title);
        return title == "STX corpus" ? null : $"parse {status}, title '{title}'";
    }
}
