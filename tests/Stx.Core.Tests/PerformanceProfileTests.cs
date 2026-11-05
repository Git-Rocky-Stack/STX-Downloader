using Stx.Core.Playback;
using Xunit;

namespace Stx.Core.Tests;

public class PerformanceProfileTests
{
    static readonly HardwareFacts Eight = new(8, false);
    static readonly HardwareFacts EightWithSr = new(8, true);
    static readonly HardwareFacts Two = new(2, false);

    [Fact]
    public void Eco_decodes_on_the_gpu_with_two_threads_and_no_enhance() =>
        Assert.Equal(new PerformanceProfile(true, 2, EnhanceLevel.Off), PerformanceProfile.For(PlaybackPerformance.Eco, Eight));

    [Fact]
    public void Balanced_uses_half_the_cores_and_may_sharpen() =>
        Assert.Equal(new PerformanceProfile(true, 4, EnhanceLevel.Sharpen), PerformanceProfile.For(PlaybackPerformance.Balanced, Eight));

    [Fact]
    public void Max_uses_every_core_and_may_upscale_with_gpu_super_resolution() =>
        Assert.Equal(new PerformanceProfile(true, 8, EnhanceLevel.Upscale), PerformanceProfile.For(PlaybackPerformance.Max, EightWithSr));

    [Fact]
    public void Max_never_upscales_without_gpu_super_resolution() =>
        Assert.Equal(EnhanceLevel.Sharpen, PerformanceProfile.For(PlaybackPerformance.Max, Eight).EnhanceCeiling);

    [Fact]
    public void Auto_on_a_two_core_pc_is_eco() =>
        Assert.Equal(PerformanceProfile.For(PlaybackPerformance.Eco, Two), PerformanceProfile.For(PlaybackPerformance.Auto, Two));

    [Fact]
    public void Auto_on_an_eight_core_pc_is_balanced() =>
        Assert.Equal(PerformanceProfile.For(PlaybackPerformance.Balanced, Eight), PerformanceProfile.For(PlaybackPerformance.Auto, Eight));

    [Fact]
    public void Auto_with_gpu_super_resolution_is_max() =>
        Assert.Equal(PerformanceProfile.For(PlaybackPerformance.Max, EightWithSr), PerformanceProfile.For(PlaybackPerformance.Auto, EightWithSr));

    [Fact]
    public void Balanced_uses_at_least_two_threads_on_one_core() =>
        Assert.Equal(2, PerformanceProfile.For(PlaybackPerformance.Balanced, new HardwareFacts(1, false)).DecoderThreads);
}
