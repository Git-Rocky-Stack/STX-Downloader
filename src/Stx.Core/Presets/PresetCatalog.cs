using Stx.Core.Audio;

namespace Stx.Core.Presets;

/// <summary>What gets embedded in a file. Each has its own switch, and none touches the audio or video itself.</summary>
public sealed record MetadataOptions(bool Tags, bool CoverArt, bool Chapters)
{
    public static readonly MetadataOptions None = new(false, false, false);
}

/// <summary>A named outcome that sets format and quality together.</summary>
public sealed record Preset(string Name, string Description, OutputChoice Output, bool Reencodes, int? MaxHeight, MetadataOptions Metadata)
{
    /// <summary>Loudness levelling changes the audio, so no preset turns it on. It's always your own choice.</summary>
    public bool LevelLoudness => false;

    public string VerdictFor(bool sourceIsLossy) =>
        Reencodes ? Verdict.For(OutputChoice.AacM4a, sourceIsLossy) : Verdict.For(Output, sourceIsLossy);
}

public static class PresetCatalog
{
    static readonly MetadataOptions Everything = new(true, true, true);

    public static readonly Preset Default = new("Keep original", "The source's own streams, untouched.", OutputChoice.Original, false, null, MetadataOptions.None);

    public static IReadOnlyList<Preset> All { get; } =
    [
        Default,
        new("Car USB", "H.264 and AAC up to 1080p, for head units that won't play much else.", OutputChoice.Mp4, true, 1080, new MetadataOptions(true, true, false)),
        new("Phone", "H.264 and AAC up to 720p, small and widely playable.", OutputChoice.Mp4, true, 720, new MetadataOptions(true, true, false)),
        new("Archive", "The source's own streams in MKV, with tags, cover art and chapters.", OutputChoice.Mkv, false, null, Everything),
    ];
}
