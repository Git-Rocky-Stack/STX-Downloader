using System.Globalization;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Stx.Playback;
using Xunit;

namespace Stx.Playback.Tests;

/// <summary>
/// MIDI through VLC's FluidSynth with the SoundFont STX ships. Nothing played MIDI in the tests before. Since
/// 2026-11-07 the SoundFont is FluidR3_GM as SoundFont 3 (its samples in Ogg Vorbis, tools/vlc/Make-Sf3.cs), which
/// only a FluidSynth built with libsndfile and libVorbis reads, so this proves the shipped engine reads it, and that it
/// sounds as the 148 MB SoundFont 2 did.
/// </summary>
public class MidiTests
{
    /// <summary>
    /// A Standard MIDI File, 10 seconds: a C major chord on piano with a snare; a run up the scale on strings with a
    /// hi-hat (noise, the hardest for Vorbis); then church organ, flute and strings held eight seconds, far past their
    /// samples' ends, so what sounds is each sample's loop.
    /// </summary>
    public static byte[] TenSeconds()
    {
        byte[] events =
        [
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, 0x00, 0xC0, 0x00,                 // 120 beats a minute; piano
            0x00, 0x90, 0x3C, 0x64, 0x00, 0x90, 0x40, 0x64, 0x00, 0x90, 0x43, 0x64, 0x00, 0x99, 0x26, 0x64,
            0x83, 0x60, 0x80, 0x3C, 0x40, 0x00, 0x80, 0x40, 0x40, 0x00, 0x80, 0x43, 0x40, 0x00, 0x89, 0x26, 0x40,
            0x00, 0xC1, 0x30,                                                           // strings
            0x00, 0x91, 0x48, 0x64, 0x00, 0x99, 0x2A, 0x64, 0x81, 0x70, 0x81, 0x48, 0x40,
            0x00, 0x91, 0x4A, 0x64, 0x00, 0x99, 0x2A, 0x64, 0x81, 0x70, 0x81, 0x4A, 0x40,
            0x00, 0x91, 0x4C, 0x64, 0x00, 0x99, 0x2A, 0x64, 0x81, 0x70, 0x81, 0x4C, 0x40,
            0x00, 0xC2, 0x13, 0x00, 0xC3, 0x49,                                         // church organ, flute
            0x00, 0x92, 0x30, 0x64, 0x00, 0x93, 0x54, 0x64, 0x00, 0x91, 0x37, 0x64,
            0xBC, 0x00, 0x82, 0x30, 0x40, 0x00, 0x83, 0x54, 0x40, 0x00, 0x81, 0x37, 0x40, // 7,680 ticks: 8 s
            0x87, 0x40, 0xFF, 0x2F, 0x00,
        ];
        byte[] header = [(byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0x01, 0xE0];
        byte[] track = [(byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, (byte)events.Length];
        return [.. header, .. track, .. events];
    }

    // The level of each second of TenSeconds rendered with FluidR3_GM.sf2, the SoundFont 2 the SF3 is made from, by
    // this engine (2026-11-07), from the first sound on. The SF3 rendered within 0.1 dB of each; one whose loop points
    // were read wrongly strayed by up to 1.4 dB.
    static readonly double[] Sf2Seconds =
        [-25.3, -26.7, -23.4, -20.7, -21.3, -20.8, -20.9, -21.2, -20.6, -21.1, -21.1, -21.1, -21.3, -20.8, -21.4, -21.3, -21.2, -20.9, -22.4, -42.6];
    const double Tolerance = 0.4;
    const int Rate = 44100;

    /// <summary>Renders a MIDI file through VLC's FluidSynth into memory: 16-bit stereo at 44.1 kHz, and what FluidSynth complained of.</summary>
    static (short[] Sound, List<string> Complaints) Render(string soundFont, byte[] midi)
    {
        string file = Path.Combine(Path.GetTempPath(), $"stx-midi-{Guid.NewGuid():N}.mid");
        File.WriteAllBytes(file, midi);
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            var complaints = new List<string>();
            using var vlc = new LibVLC("--no-lua", "--soundfont=" + soundFont);
            vlc.Log += (_, e) =>
            {
                if (e.Level >= LogLevel.Warning && e.Module?.Contains("fluidsynth", StringComparison.OrdinalIgnoreCase) == true)
                    lock (complaints) complaints.Add(e.Message);
            };
            using var media = new Media(vlc, file, FromType.FromPath);
            using var player = new MediaPlayer(media);
            var sound = new List<short>();
            player.SetAudioFormat("S16N", Rate, 2);
            player.SetAudioCallbacks((_, samples, count, _) =>
            {
                var chunk = new short[count * 2];
                Marshal.Copy(samples, chunk, 0, chunk.Length);
                lock (sound) sound.AddRange(chunk);
            }, null, null, null, null);
            Assert.True(player.Play());
            Fixture.Within(TimeSpan.FromSeconds(25), () => player.State is VLCState.Ended or VLCState.Error);
            player.Stop();
            lock (sound) lock (complaints) return ([.. sound], [.. complaints]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Midi_plays_with_the_shipped_soundfont_as_the_sf2_did()
    {
        Assert.EndsWith(".sf3", LibVlcEngine.ShippedSoundFont, StringComparison.Ordinal);
        Assert.True(File.Exists(LibVlcEngine.ShippedSoundFont), LibVlcEngine.ShippedSoundFont);
        var (sound, complaints) = Render(LibVlcEngine.ShippedSoundFont, TenSeconds());
        Assert.Empty(complaints);

        // Measured from the first sound above a whisper, as the reference was, a second (one stereo frame per sample) at a time.
        int from = Array.FindIndex(sound, s => Math.Abs((int)s) > 200) & ~1;
        Assert.True(from >= 0, "FluidSynth made no sound");
        var heard = new double[Sf2Seconds.Length];
        for (int w = 0; w < heard.Length; w++)
        {
            int start = from + w * Rate, end = Math.Min(sound.Length, start + Rate);
            double energy = 0;
            for (int i = start; i < end; i++) energy += (double)sound[i] * sound[i];
            heard[w] = end > start ? 10 * Math.Log10(energy / (end - start) / ((double)short.MaxValue * short.MaxValue) + 1e-12) : -120;
        }
        string Show(IEnumerable<double> levels) => string.Join(", ", levels.Select(l => l.ToString("F1", CultureInfo.InvariantCulture)));
        Assert.True(heard.Zip(Sf2Seconds).All(p => Math.Abs(p.First - p.Second) <= Tolerance),
            $"The SoundFont doesn't sound as FluidR3_GM.sf2 did.{Environment.NewLine}heard: {Show(heard)}{Environment.NewLine}sf2:   {Show(Sf2Seconds)}");
    }
}
