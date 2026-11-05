using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

/// <summary>Phase 6: ENHANCE says what runs, and never fakes UPSCALE on a GPU without super resolution.</summary>
public class EnhanceTests
{
    static readonly PerformanceProfile Balanced = new(true, 4, EnhanceLevel.Sharpen);
    static readonly PerformanceProfile Max = new(true, 8, EnhanceLevel.Upscale);
    static readonly PerformanceProfile Eco = new(true, 2, EnhanceLevel.Off);
    // The reference machine's GPU.
    static readonly GpuFacts Uhd620 = new(GpuFacts.Intel, "Intel(R) UHD Graphics 620", new Version(31, 0, 101, 2125));
    static readonly GpuFacts Rtx = new(GpuFacts.Nvidia, "NVIDIA GeForce RTX 4070", new Version(31, 0, 15, 3640));

    [Theory]
    [InlineData(GpuFacts.Nvidia, "NVIDIA GeForce RTX 4070", "31.0.15.3640", true)]
    [InlineData(GpuFacts.Nvidia, "NVIDIA GeForce RTX 3060", "31.0.15.2802", false)] // 528.02, before VSR
    [InlineData(GpuFacts.Nvidia, "NVIDIA GeForce GTX 1660", "31.0.15.3640", false)]
    [InlineData(GpuFacts.Intel, "Intel(R) UHD Graphics 620", "31.0.101.2125", false)]
    [InlineData(GpuFacts.Intel, "Intel(R) Iris(R) Xe Graphics", "31.0.101.5186", true)]
    [InlineData(GpuFacts.Intel, "Intel(R) Arc(TM) A770 Graphics", "31.0.101.5186", true)]
    [InlineData(GpuFacts.Amd, "AMD Radeon RX 7800 XT", "31.0.24027.1012", false)]
    public void Super_resolution_is_only_claimed_where_it_exists(int vendor, string name, string driver, bool available) =>
        Assert.Equal(available, Enhance.SuperResolution(new GpuFacts(vendor, name, Version.Parse(driver))));

    [Fact]
    public void Nvidias_driver_number_is_read_from_windows_version() => Assert.Equal(536.40, Rtx.NvidiaDriver);

    // Done when: UPSCALE either applies or, on hardware without GPU super resolution (the reference UHD 620), is shown as unavailable, not faked.
    [Fact]
    public void Upscale_on_the_reference_gpu_runs_nothing_and_says_so()
    {
        Assert.Equal(EnhanceLevel.Off, Enhance.Effective(EnhanceLevel.Upscale, Max, Enhance.SuperResolution(Uhd620)));
        Assert.Equal("UPSCALE UNAVAILABLE: INTEL(R) UHD GRAPHICS 620 HAS NO SUPER RESOLUTION", Enhance.Status(EnhanceLevel.Upscale, Max, Uhd620, null));
    }

    [Fact]
    public void Performance_sets_the_ceiling_and_the_readout_says_so()
    {
        Assert.Equal(EnhanceLevel.Sharpen, Enhance.Effective(EnhanceLevel.Upscale, Balanced, superResolution: true));
        Assert.Equal(EnhanceLevel.Off, Enhance.Effective(EnhanceLevel.Sharpen, Eco, superResolution: true));
        Assert.StartsWith("OFF: PERFORMANCE ECO", Enhance.Status(EnhanceLevel.Sharpen, Eco, Rtx, null));
        Assert.StartsWith("SHARPEN ONLY", Enhance.Status(EnhanceLevel.Upscale, Balanced, Rtx, null));
    }

    [Fact]
    public void The_readout_waits_for_vlc_to_confirm()
    {
        Assert.Equal("SHARPEN · SOFTWARE FILTER · WAITING FOR A PICTURE", Enhance.Status(EnhanceLevel.Sharpen, Balanced, Uhd620, null));
        Assert.Equal("SHARPEN · SOFTWARE FILTER · ON SCREEN ONLY", Enhance.Status(EnhanceLevel.Sharpen, Balanced, Uhd620, EnhanceLevel.Sharpen));
        Assert.Equal("UPSCALE · NVIDIA GEFORCE RTX 4070 SUPER RESOLUTION · ON SCREEN ONLY", Enhance.Status(EnhanceLevel.Upscale, Max, Rtx, EnhanceLevel.Upscale));
    }

    // VLC can still refuse (an old driver, a format its scaler doesn't take); then the readout says so.
    [Fact]
    public void A_refusal_from_vlc_is_shown_not_hidden() =>
        Assert.Equal("UPSCALE UNAVAILABLE: VLC'S ENGINE COULDN'T START SUPER RESOLUTION", Enhance.Status(EnhanceLevel.Upscale, Max, Rtx, EnhanceLevel.Off));

    [Fact]
    public void Each_level_is_one_engine_option()
    {
        Assert.Empty(Enhance.Options(EnhanceLevel.Off));
        Assert.Equal(["--video-filter=sharpen", "--sharpen-sigma=0.6"], Enhance.Options(EnhanceLevel.Sharpen));
        Assert.Equal(["--d3d11-upscale-mode=super"], Enhance.Options(EnhanceLevel.Upscale));
    }

    // Spec anti-goal: any wording implying an upscale improved the file.
    [Fact]
    public void Nothing_claims_the_file_got_better()
    {
        foreach (var wanted in Enum.GetValues<EnhanceLevel>())
            foreach (var seen in new EnhanceLevel?[] { null, EnhanceLevel.Off, EnhanceLevel.Sharpen, EnhanceLevel.Upscale })
            {
                string text = Enhance.Status(wanted, Max, Rtx, seen);
                foreach (string word in new[] { "IMPROV", "BETTER", "ENHANCED", "HD", "4K", "QUALITY" })
                    Assert.DoesNotContain(word, text.Replace("SHARPEN", "", StringComparison.Ordinal));
            }
    }
}
