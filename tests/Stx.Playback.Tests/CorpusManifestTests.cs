using System.Text.Json;
using System.Text.RegularExpressions;

namespace Stx.Playback.Tests;

/// <summary>The corpus's paperwork: every VideoLAN feature has a file, and every downloaded file is pinned. Runs in CI.</summary>
public partial class CorpusManifestTests
{
    [Fact]
    public void Every_feature_has_a_corpus_file()
    {
        var covered = Corpus.Manifest().SelectMany(e => e.Features).ToHashSet(StringComparer.Ordinal);
        var missing = Corpus.Features().Where(f => f.Untestable is null && !covered.Contains(f.Name)).Select(f => f.Name).ToList();
        Assert.True(missing.Count == 0, "No corpus file for: " + string.Join(", ", missing));
    }

    // Phase 3: STX registers as a player only for types it's proven to play. Each one has a corpus file of that type.
    [Fact]
    public void Every_type_STX_registers_is_played_by_a_corpus_file()
    {
        var proven = Corpus.Manifest().Where(e => e.Expect != "refused").Select(e => Path.GetExtension(e.File).ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var unproven = Stx.Core.Library.MediaTypes.All.Where(t => !proven.Contains(t)).ToList();
        Assert.True(unproven.Count == 0, "Registered without a corpus file: " + string.Join(", ", unproven));
    }

    [Fact]
    public void Every_manifest_feature_is_on_the_list()
    {
        var listed = Corpus.Features().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        var stray = Corpus.Manifest().SelectMany(e => e.Features).Where(f => !listed.Contains(f)).Distinct().ToList();
        Assert.True(stray.Count == 0, "Manifest names features that aren't in vlc-features.json: " + string.Join(", ", stray));
    }

    [Fact]
    public void Untestable_features_say_why()
    {
        var bare = Corpus.Features().Where(f => f.Untestable is { } why && why.Trim().Length < 20).Select(f => f.Name).ToList();
        Assert.True(bare.Count == 0, "Untestable without a reason: " + string.Join(", ", bare));
    }

    [Fact]
    public void Fetched_files_are_pinned()
    {
        var loose = Corpus.Manifest()
            .Where(e => e.Source == "fetched")
            .Where(e => e.Url is null || !e.Url.StartsWith("https://", StringComparison.Ordinal) || e.Sha256 is null || !Sha256().IsMatch(e.Sha256) || string.IsNullOrWhiteSpace(e.License))
            .Select(e => e.File).ToList();
        Assert.True(loose.Count == 0, "Fetched without an https url, a sha256 or a license: " + string.Join(", ", loose));
    }

    [Fact]
    public void Generated_files_say_how()
    {
        var bare = Corpus.Manifest().Where(e => e.Source == "generated" && string.IsNullOrWhiteSpace(e.Make)).Select(e => e.File).ToList();
        Assert.True(bare.Count == 0, "Generated without a make line: " + string.Join(", ", bare));
    }

    [Fact]
    public void Real_world_profiles_are_present()
    {
        var all = Corpus.Manifest().SelectMany(e => e.Features).ToHashSet(StringComparer.Ordinal);
        foreach (var profile in new[] { "HEVC Main10", "HDR10 (PQ, BT.2020)", "AV1", "VP9" })
            Assert.Contains(profile, all);
    }

    [Fact]
    public void The_feature_list_records_where_and_when_it_was_read()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Corpus.FeaturesPath));
        Assert.Equal("https://www.videolan.org/vlc/features.html", doc.RootElement.GetProperty("source").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", doc.RootElement.GetProperty("read").GetString());
        Assert.Equal("3.0.24", doc.RootElement.GetProperty("libvlc").GetString());
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}
