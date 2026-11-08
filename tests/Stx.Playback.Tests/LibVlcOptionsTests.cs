using Stx.Core.Playback;

namespace Stx.Playback.Tests;

public class LibVlcOptionsTests
{
    static readonly PerformanceProfile Gpu = new(true, 4, EnhanceLevel.Sharpen);
    static readonly PerformanceProfile Cpu = new(false, 2, EnhanceLevel.Off);

    [Fact]
    public void Lua_is_off_and_nothing_discovers_or_streams_out()
    {
        foreach (var options in new[] { LibVlcOptions.For(headless: false), LibVlcOptions.For(headless: true) })
        {
            Assert.Contains("--no-lua", options);
            Assert.DoesNotContain(options, o => o.StartsWith("--sd=", StringComparison.Ordinal) || o.StartsWith("--sout", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Media_options_carry_the_profile()
    {
        Assert.Contains(":avcodec-hw=d3d11va", LibVlcOptions.ForMedia(Gpu));
        Assert.Contains(":avcodec-threads=4", LibVlcOptions.ForMedia(Gpu));
        Assert.Contains(":avcodec-hw=none", LibVlcOptions.ForMedia(Cpu));
        Assert.Contains(":avcodec-threads=2", LibVlcOptions.ForMedia(Cpu));
    }

    // LibVLC 3.0.24's dummy video output crashes the process (0xC0000005) when an H.264 file ends, so a headless
    // engine draws into a small memory buffer instead (LibVlcEngine), and only the sound goes to the dummy output.
    [Fact]
    public void Headless_never_uses_the_dummy_video_output()
    {
        Assert.Contains("--aout=dummy", LibVlcOptions.For(headless: true));
        Assert.DoesNotContain(LibVlcOptions.For(headless: true), o => o.StartsWith("--vout", StringComparison.Ordinal));
        Assert.DoesNotContain(LibVlcOptions.For(headless: false), o => o.EndsWith("=dummy", StringComparison.Ordinal));
    }

    [Fact]
    public void Real_audio_drops_the_dummy_sound()
    {
        Assert.DoesNotContain("--aout=dummy", LibVlcOptions.For(headless: true, realAudio: true));
    }

    [Fact]
    public void The_soundfont_is_passed_when_given()
    {
        Assert.Contains(@"--soundfont=C:\STX\libvlc\soundfonts\FluidR3_GM.sf3", LibVlcOptions.For(headless: false, soundFont: @"C:\STX\libvlc\soundfonts\FluidR3_GM.sf3"));
        Assert.DoesNotContain(LibVlcOptions.For(headless: false), o => o.StartsWith("--soundfont", StringComparison.Ordinal));
    }

    [Fact]
    public void Swap_chain_options_are_appended()
    {
        string[] swapChain = ["--winrt-d3dcontext=0x1", "--winrt-swapchain=0x2"];
        var options = LibVlcOptions.For(headless: false, swapChain);
        Assert.Contains("--winrt-d3dcontext=0x1", options);
        Assert.Contains("--winrt-swapchain=0x2", options);
    }
}
