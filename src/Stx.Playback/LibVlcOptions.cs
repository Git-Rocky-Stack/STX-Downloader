using System.Globalization;
using Stx.Core.Playback;

namespace Stx.Playback;

/// <summary>The command line LibVLC is started with, and the options each media carries.</summary>
public static class LibVlcOptions
{
    /// <summary>
    /// Options for the LibVLC instance. Lua site scripts stay off, so web links keep using the capture path,
    /// and nothing discovers services or streams out, so no network port is opened.
    /// </summary>
    /// <param name="headless">No screen and no sound: for tests and for anything that has no screen. Pictures go to a
    /// memory buffer the engine sets up, never to LibVLC's dummy video output, which crashes when an H.264 file ends.</param>
    /// <param name="swapChain">The options the video view hands over once its swap chain exists (spike S1).</param>
    /// <param name="realAudio">With <paramref name="headless"/>, keep the real sound output.</param>
    /// <param name="soundFont">The SoundFont FluidSynth plays MIDI with; MIDI can't play without one.</param>
    public static string[] For(bool headless, IReadOnlyList<string>? swapChain = null, bool realAudio = false, string? soundFont = null)
    {
        var options = new List<string> { "--no-lua" };
        if (headless && !realAudio) options.Add("--aout=dummy");
        if (soundFont is not null) options.Add("--soundfont=" + soundFont);
        if (swapChain is not null) options.AddRange(swapChain);
        return [.. options];
    }

    /// <summary>
    /// Options for what's being opened. Discs start at their main title: their menus need a pointer on the picture,
    /// which a swap chain doesn't pass back. RTSP runs over its one TCP connection, so no port is opened for RTP.
    /// </summary>
    public static string[] ForTarget(TargetKind kind) => kind switch
    {
        TargetKind.Disc => [":no-dvdnav-menu", ":no-bluray-menu"],
        TargetKind.Stream => [":rtsp-tcp"],
        _ => [],
    };

    /// <summary>The PERFORMANCE profile, carried by each media so it can change without a new LibVLC.</summary>
    public static string[] ForMedia(PerformanceProfile profile) =>
    [
        ":avcodec-hw=" + (profile.HardwareDecoding ? "d3d11va" : "none"),
        ":avcodec-threads=" + profile.DecoderThreads.ToString(CultureInfo.InvariantCulture),
    ];
}
