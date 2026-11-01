using System.Globalization;
using Stx.Core.Errors;

namespace Stx.Tools;

public sealed record ConvertResult(bool IsSuccess, ErrorKind? Error, string? OutputPath)
{
    public static ConvertResult Saved(string path) => new(true, null, path);
    public static ConvertResult Failed(ErrorKind kind) => new(false, kind, null);
}

/// <summary>
/// Runs one conversion. It writes to a temporary name and renames only on success, so a failed or canceled
/// job never leaves a broken file that looks finished, and the original is never touched.
/// </summary>
public sealed class FfmpegRunner(IProcessRunner process, string ffmpegPath, Func<Task<string>>? h264Encoder = null)
{
    const int LinesKeptForDiagnosis = 200;
    const string ProgressKey = "out_time_ms=";

    public event EventHandler<string>? LogLine;

    public async Task<ConvertResult> ConvertAsync(ConvertRequest request, Action<double>? onProgress = null, CancellationToken cancel = default)
    {
        if (!File.Exists(request.InputPath)) return ConvertResult.Failed(ErrorKind.ToolFailure);
        try
        {
            Directory.CreateDirectory(request.OutputFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ConvertResult.Failed(ErrorKind.OutputFailure);
        }

        string extension = FfmpegArgs.Extension(request.Output);
        if (extension.Length == 0) extension = Path.GetExtension(request.InputPath);
        string stem = Path.GetFileNameWithoutExtension(request.InputPath);
        string partial = Path.Combine(request.OutputFolder, $"{stem}.partial-{Guid.NewGuid():N}{extension}");
        string encoder = request.Reencode && h264Encoder is not null ? await h264Encoder().ConfigureAwait(false) : "libx264";

        var recent = new Queue<string>();
        try
        {
            int exit = await process.RunAsync(ffmpegPath, FfmpegArgs.ForConvert(request, partial, encoder), line => OnLine(line, recent, request.DurationSeconds, onProgress), cancel).ConfigureAwait(false);
            if (exit != 0)
            {
                string evidence;
                lock (recent) evidence = string.Join('\n', recent);
                return ConvertResult.Failed(ErrorClassifier.Classify(evidence, exit));
            }

            string target = FreeName(request.OutputFolder, stem, extension);
            File.Move(partial, target);
            return ConvertResult.Saved(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ConvertResult.Failed(ErrorKind.OutputFailure);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }

    void OnLine(string line, Queue<string> recent, double? duration, Action<double>? onProgress)
    {
        lock (recent)
        {
            recent.Enqueue(line);
            if (recent.Count > LinesKeptForDiagnosis) recent.Dequeue();
        }
        LogLine?.Invoke(this, line);

        // Despite its name, ffmpeg reports out_time_ms in microseconds.
        if (duration is > 0 && line.StartsWith(ProgressKey, StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(ProgressKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros))
            onProgress?.Invoke(Math.Clamp(micros / 1_000_000.0 / duration.Value * 100, 0, 100));
    }

    static string FreeName(string folder, string stem, string extension)
    {
        string path = Path.Combine(folder, stem + extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){extension}");
        return path;
    }
}
