namespace Stx.Core.Playback;

/// <summary>The PERFORMANCE setting. Auto is the default and picks from the hardware.</summary>
public enum PlaybackPerformance { Auto, Eco, Balanced, Max }

/// <summary>How far picture enhancement may go. Upscale needs GPU super resolution.</summary>
public enum EnhanceLevel { Off, Sharpen, Upscale }

public sealed record HardwareFacts(int LogicalCores, bool GpuSuperResolution);

/// <summary>What a PERFORMANCE level means on this PC: GPU decoding, decoder threads, and the enhance ceiling.</summary>
public sealed record PerformanceProfile(bool HardwareDecoding, int DecoderThreads, EnhanceLevel EnhanceCeiling)
{
    public static PerformanceProfile For(PlaybackPerformance level, HardwareFacts hw) => level switch
    {
        PlaybackPerformance.Eco => new(true, 2, EnhanceLevel.Off),
        PlaybackPerformance.Balanced => new(true, Math.Max(2, hw.LogicalCores / 2), EnhanceLevel.Sharpen),
        PlaybackPerformance.Max => new(true, Math.Max(2, hw.LogicalCores), hw.GpuSuperResolution ? EnhanceLevel.Upscale : EnhanceLevel.Sharpen),
        _ => For(Choose(hw), hw),
    };

    // Auto never asks for more than the hardware can keep smooth: two cores or fewer stay on Eco.
    static PlaybackPerformance Choose(HardwareFacts hw) =>
        hw.LogicalCores <= 2 ? PlaybackPerformance.Eco
        : hw.GpuSuperResolution ? PlaybackPerformance.Max
        : PlaybackPerformance.Balanced;
}
