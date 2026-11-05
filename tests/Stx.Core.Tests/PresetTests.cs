using Stx.Core.Audio;
using Stx.Core.Presets;
using Xunit;

namespace Stx.Core.Tests;

public class PresetTests
{
    static Preset Named(string name) => PresetCatalog.All.Single(p => p.Name == name);

    [Fact]
    public void All_named_presets_exist() =>
        Assert.Equal(["Keep original", "Car USB", "Phone", "Archive", "Shrink AV1", "Shrink HEVC", "Shrink Opus"], PresetCatalog.All.Select(p => p.Name));

    [Fact]
    public void Keep_original_preset_never_reencodes()
    {
        var keep = Named("Keep original");
        Assert.False(keep.Reencodes);
        Assert.Equal(OutputChoice.Original, keep.Output);
        Assert.Equal("Bit-perfect", keep.VerdictFor(sourceIsLossy: true));
    }

    [Fact]
    public void Keep_original_is_the_default() => Assert.Equal("Keep original", PresetCatalog.Default.Name);

    [Theory]
    [InlineData("Car USB", 1080)]
    [InlineData("Phone", 720)]
    public void Car_usb_and_phone_reencode_to_h264_aac(string name, int height)
    {
        var preset = Named(name);
        Assert.True(preset.Reencodes);
        Assert.Equal(OutputChoice.Mp4, preset.Output);
        Assert.Equal(height, preset.MaxHeight);
        Assert.Equal("Re-encoded; slight quality loss", preset.VerdictFor(sourceIsLossy: true));
    }

    [Fact]
    public void Archive_keeps_the_original_streams_and_everything_about_them()
    {
        var archive = Named("Archive");
        Assert.False(archive.Reencodes);
        Assert.Equal(OutputChoice.Mkv, archive.Output);
        Assert.Equal(new MetadataOptions(Tags: true, CoverArt: true, Chapters: true), archive.Metadata);
        Assert.Equal("Bit-perfect", archive.VerdictFor(sourceIsLossy: true));
    }

    [Fact]
    public void No_preset_levels_loudness_by_surprise() =>
        Assert.All(PresetCatalog.All, p => Assert.False(p.LevelLoudness));

    [Fact]
    public void Each_preset_says_what_it_does() =>
        Assert.All(PresetCatalog.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
}
