using Stx.Core.Playback;

namespace Stx.Playback;

/// <summary>What AUTO chooses from: this PC's logical cores. GPU super resolution is detected in a later phase; until then it reads as absent.</summary>
public static class HardwareProbe
{
    public static HardwareFacts Detect() => new(Environment.ProcessorCount, GpuSuperResolution: false);
}
