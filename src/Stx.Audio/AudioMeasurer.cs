using System.Diagnostics;
using System.Text;
using Stx.Tools;

namespace Stx.Audio;

/// <summary>Measures a file with FFmpeg and FFprobe at below-normal priority, so analysis never competes with what you're doing.</summary>
public sealed class AudioMeasurer(string ffmpegPath, string ffprobePath)
{
    const int AnalysisChannels = 2;

    readonly ProcessRunner _process = new(ProcessPriorityClass.BelowNormal);

    /// <summary>Null when the file has no audio stream or can't be read.</summary>
    public async Task<AudioFacts?> MeasureAsync(string path, CancellationToken cancel = default)
    {
        var probe = ProbeParser.Parse(await Output(ffprobePath, ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path], cancel).ConfigureAwait(false));
        if (probe is null) return null;

        int rate = probe.SampleRate is > 0 ? probe.SampleRate.Value : 48000;
        var spectrum = await Task.Run(() => Spectrum(path, rate, cancel), cancel).ConfigureAwait(false);
        if (spectrum is null) return null;
        var loudness = LoudnessParser.Parse(await Output(ffmpegPath, LoudnessArgs(path), cancel).ConfigureAwait(false));
        return AudioFacts.Measured(probe, spectrum, loudness);
    }

    public static IReadOnlyList<string> LoudnessArgs(string path) =>
        ["-hide_banner", "-nostdin", "-nostats", "-i", path, "-map", "0:a:0", "-af", "ebur128=peak=true", "-f", "null", "-"];

    async Task<string> Output(string tool, IReadOnlyList<string> args, CancellationToken cancel)
    {
        var text = new StringBuilder();
        await _process.RunAsync(tool, args, line => { lock (text) text.AppendLine(line); }, cancel).ConfigureAwait(false);
        lock (text) return text.ToString();
    }

    // Null when FFmpeg stopped with an error before the end: what it gave until then isn't the file's spectrum, and
    // used to be shown as "Measured" all the same (review 2026-11-07).
    SpectrumResult? Spectrum(string path, int rate, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", path, "-map", "0:a:0", "-ac", "2", "-ar", rate.ToString(System.Globalization.CultureInfo.InvariantCulture), "-f", "f32le", "-" })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (InvalidOperationException) { }
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();

        using var stop = cancel.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var result = SpectrumAnalyzer.Analyze(process.StandardOutput.BaseStream, rate, AnalysisChannels);
        process.WaitForExit();
        cancel.ThrowIfCancellationRequested();
        return process.ExitCode == 0 ? result : null;
    }
}
