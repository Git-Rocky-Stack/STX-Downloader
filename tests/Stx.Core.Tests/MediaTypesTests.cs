using Stx.Core.Library;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>The choices MediaTypes makes where an extension could go either way. The corpus proves each type plays.</summary>
public class MediaTypesTests
{
    // .ogx is Ogg's multiplexed spelling, so it's video.
    [Theory]
    [InlineData("tune.XM", MediaKind.Audio)]
    [InlineData("book.m4b", MediaKind.Audio)]
    [InlineData("ring.m4r", MediaKind.Audio)]
    [InlineData("memo.3ga", MediaKind.Audio)]
    [InlineData("clip.ogx", MediaKind.Video)]
    [InlineData("show.dvr-ms", MediaKind.Video)]
    [InlineData("show.wtv", MediaKind.Video)]
    [InlineData("raw.h264", MediaKind.Video)]
    public void Each_type_has_its_kind(string path, MediaKind kind) => Assert.Equal(kind, MediaTypes.KindOf(path));

    // Left out: types the engine doesn't decode, protected ones, and extensions other files use as often.
    [Theory]
    [InlineData(".dsf")]
    [InlineData(".dff")]
    [InlineData(".mp4v")]
    [InlineData(".vgz")]
    [InlineData(".m4p")]
    [InlineData(".xa")]
    [InlineData(".sid")]
    [InlineData(".spc")]
    [InlineData(".nsf")]
    [InlineData(".stm")]
    [InlineData(".ps")]
    [InlineData(".rec")]
    [InlineData(".bin")]
    [InlineData(".sub")]
    [InlineData(".idx")]
    // Tracker music, camcorder video and Go's go.mod: either kind would mislabel the other two (review 2026-11-12).
    [InlineData(".mod")]
    public void Ambiguous_protected_and_unplayable_types_are_left_out(string extension)
    {
        Assert.False(MediaTypes.IsMedia("file" + extension));
        Assert.DoesNotContain(extension, MediaTypes.All);
    }

    [Fact]
    public void The_list_is_lower_case_sorted_and_unique()
    {
        Assert.All(MediaTypes.All, t => Assert.Equal(t.ToLowerInvariant(), t));
        Assert.Equal(MediaTypes.All.Order(StringComparer.Ordinal), MediaTypes.All);
        Assert.Equal(MediaTypes.All.Count, MediaTypes.All.Distinct().Count());
    }
}
