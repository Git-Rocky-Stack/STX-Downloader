namespace Stx.Playback.Tests;

/// <summary>
/// Every test class that opens the real sound device runs in this one collection, so its tests run one at a time.
/// A bit-perfect test holds the device exclusively; a shared test running beside it found WASAPI taken and VLC fell
/// back to DirectSound (seen 2026-11-05, once exclusive mode was allowed on the reference PC).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SoundDeviceCollection
{
    public const string Name = "Sound device";
}
