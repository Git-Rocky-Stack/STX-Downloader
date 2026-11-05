using System.Globalization;

namespace Stx.Core.Playback;

/// <summary>The graphics adapter LibVLC draws with: its vendor, its name, and its driver's version as Windows reports it.</summary>
public sealed record GpuFacts(int VendorId, string Name, Version? Driver)
{
    public const int Nvidia = 0x10DE, Intel = 0x8086, Amd = 0x1002;

    /// <summary>NVIDIA's own driver number (536.40) from Windows' (31.0.15.3640): the last five digits.</summary>
    public double? NvidiaDriver => VendorId == Nvidia && Driver is { } v && v.Build >= 0 && v.Revision >= 0
        ? (v.Build % 10 * 10000 + v.Revision) / 100.0
        : null;
}

/// <summary>
/// ENHANCE (Phase 6): OFF, SHARPEN or UPSCALE. It changes only the picture on screen, never a file. Spike S6 read VLC
/// 3.0.24's source: UPSCALE is its Direct3D 11 "Super Resolution" scaler (--d3d11-upscale-mode=super,
/// modules/video_output/win32/direct3d11.c:77-80), which uses NVIDIA's RTX Video Super Resolution with a 530 or later
/// driver and Intel's on hardware that has it (d3d11_scaler.cpp:76-123); anywhere else it quietly falls back to plain
/// scaling. SHARPEN is VLC's software sharpen filter. These are options of the whole engine, not of one file
/// (src/input/resource.c:241 makes the video output under the player), so a change restarts the engine in place.
/// </summary>
public static class Enhance
{
    /// <summary>In the order of EnhanceLevel.</summary>
    public static IReadOnlyList<string> Labels { get; } = ["OFF", "SHARPEN", "UPSCALE"];

    /// <summary>
    /// Whether this GPU has super resolution VLC can use. VLC itself assumes any Intel GPU does ("detection doesn't
    /// work"); STX doesn't, so a GPU without it, such as the reference UHD 620, is shown as unavailable instead of
    /// pretending: NVIDIA RTX with driver 530 or later, and Intel Arc or Xe.
    /// </summary>
    public static bool SuperResolution(GpuFacts? gpu) => gpu switch
    {
        { VendorId: GpuFacts.Nvidia } g => g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) && g.NvidiaDriver >= 530,
        { VendorId: GpuFacts.Intel } g => g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) || g.Name.Contains("Xe", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>What actually runs: the knob, held under PERFORMANCE's ceiling, and UPSCALE only where the GPU has it.</summary>
    public static EnhanceLevel Effective(EnhanceLevel wanted, PerformanceProfile profile, bool superResolution)
    {
        var level = wanted > profile.EnhanceCeiling ? profile.EnhanceCeiling : wanted;
        return level == EnhanceLevel.Upscale && !superResolution ? EnhanceLevel.Off : level;
    }

    /// <summary>The engine's options for a level. OFF adds nothing, so LibVLC scales as it always has.</summary>
    public static string[] Options(EnhanceLevel level) => level switch
    {
        EnhanceLevel.Sharpen => ["--video-filter=sharpen", "--sharpen-sigma=" + 0.6.ToString(CultureInfo.InvariantCulture)],
        EnhanceLevel.Upscale => ["--d3d11-upscale-mode=super"],
        _ => [],
    };

    /// <summary>What the knob's readout says: what's running, by what, or plainly why not. Never a claim the file improved.</summary>
    /// <param name="seen">What VLC's own log confirmed is running, or null before a picture has been drawn.</param>
    public static string Status(EnhanceLevel wanted, PerformanceProfile profile, GpuFacts? gpu, EnhanceLevel? seen)
    {
        bool superResolution = SuperResolution(gpu);
        if (wanted == EnhanceLevel.Off) return "OFF";
        if (wanted > profile.EnhanceCeiling)
            return profile.EnhanceCeiling == EnhanceLevel.Off ? "OFF: PERFORMANCE ECO KEEPS THE PICTURE AS IT IS" : "SHARPEN ONLY: PERFORMANCE SETS THE LIMIT";
        if (wanted == EnhanceLevel.Upscale && !superResolution)
            return gpu is null ? "UPSCALE UNAVAILABLE: NO GPU FOUND" : $"UPSCALE UNAVAILABLE: {gpu.Name.ToUpperInvariant()} HAS NO SUPER RESOLUTION";
        if (seen is { } running && running != wanted)
            return wanted == EnhanceLevel.Upscale ? "UPSCALE UNAVAILABLE: VLC'S ENGINE COULDN'T START SUPER RESOLUTION" : "SHARPEN DIDN'T START";
        string by = wanted == EnhanceLevel.Upscale ? $"{gpu!.Name.ToUpperInvariant()} SUPER RESOLUTION" : "SOFTWARE FILTER";
        return seen is null ? $"{Labels[(int)wanted]} · {by} · WAITING FOR A PICTURE" : $"{Labels[(int)wanted]} · {by} · ON SCREEN ONLY";
    }
}
