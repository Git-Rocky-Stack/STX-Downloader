using Xunit;
using Stx.Core.Playback;

namespace Stx.Core.Tests;

/// <summary>
/// Review 2026-11-07: the Dirac guard trusted LibVLC's parse, and treated a parse that timed out or threw as "not
/// Dirac", which let the file reach the decoder that crashes the process. The fallback reads the bytes itself.
/// </summary>
public class DiracSniffTests
{
    static string Playback(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "fixtures", "playback", name);
    }

    [Fact]
    public void A_dirac_file_is_found_by_its_bytes() => Assert.True(DiracSniff.Present(Playback("tiny-dirac.mkv")));

    [Theory]
    [InlineData("tiny.mp4")]
    [InlineData("moving.mp4")]
    [InlineData("tiny-audio.flac")]
    [InlineData("surround-5.1.flac")]
    [InlineData("truncated.mp4")]
    public void Other_files_are_not(string name) => Assert.False(DiracSniff.Present(Playback(name)));

    // FFmpeg writes the sequence header near the start in MOV, MP4, TS, NUT and AVI, which have no "V_DIRAC".
    [Fact]
    public void The_sequence_header_alone_is_enough()
    {
        var bytes = new byte[8192];
        new Random(7).NextBytes(bytes);
        bytes.AsSpan().Replace((byte)'B', (byte)'b');
        "BBCD\0"u8.CopyTo(bytes.AsSpan(5000));
        Assert.True(DiracSniff.Present(bytes));
        Assert.False(DiracSniff.Present(bytes.AsSpan(0, 5000)));
    }

    [Fact]
    public void A_file_that_isnt_there_is_not_dirac() => Assert.False(DiracSniff.Present(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mkv")));
}
