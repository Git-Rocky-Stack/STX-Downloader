namespace Stx.Core.Audio;

public enum OutputChoice { Original, Flac, Alac, Wav, Aiff, AacM4a, Mp3, Opus, OggVorbis, Mp4, Mkv }

/// <summary>What an output really does to quality. The wording stays honest: no format can add back what the source lost.</summary>
public static class Verdict
{
    public static string For(OutputChoice output, bool sourceIsLossy) => output switch
    {
        OutputChoice.Original or OutputChoice.Mp4 or OutputChoice.Mkv => "Bit-perfect",
        OutputChoice.Flac or OutputChoice.Alac => sourceIsLossy ? "Lossless copy of a lossy source; no quality gained" : "Lossless; nothing lost",
        OutputChoice.Wav or OutputChoice.Aiff => "Larger file, same sound",
        _ => "Re-encoded; slight quality loss",
    };
}
