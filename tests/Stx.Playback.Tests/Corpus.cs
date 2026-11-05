using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stx.Playback.Tests;

/// <summary>One VideoLAN-listed feature. Untestable carries the reason when no lawful sample exists.</summary>
public sealed record Feature(string Name, string Group, string? Untestable);

/// <summary>
/// One corpus file. Video and Audio say which streams must decode. Expect is "play" (the default), "subtitle"
/// (a subtitle track must appear, from the file or a companion beside it), "title" (the tag's title must read
/// "STX corpus"), "refused" (STX must refuse it rather than crash; Dirac) or "companion" (a subtitle file VLC loads beside the video of the same name; never opened alone). Generated files carry the Make line; fetched files carry an https url, a sha256 and a license.
/// </summary>
public sealed record Entry(
    string File,
    string[] Features,
    string Source,
    bool Video,
    bool Audio,
    string? Expect,
    string? Make,
    string? Url,
    string? Sha256,
    string? License);

static class Corpus
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip };

    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "corpus");
    }

    public static string FeaturesPath => Path.Combine(Root(), "vlc-features.json");
    public static string ManifestPath => Path.Combine(Root(), "manifest.json");
    public static string FilesDir => Path.Combine(Root(), "files");

    sealed record FeatureFile(string Source, string Read, string Libvlc, Feature[] Features);
    sealed record ManifestFile(Entry[] Entries);

    public static Feature[] Features() => JsonSerializer.Deserialize<FeatureFile>(System.IO.File.ReadAllText(FeaturesPath), Json)!.Features;
    public static Entry[] Manifest() => JsonSerializer.Deserialize<ManifestFile>(System.IO.File.ReadAllText(ManifestPath), Json)!.Entries;
}
