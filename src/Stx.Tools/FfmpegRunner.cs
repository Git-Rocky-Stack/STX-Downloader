using System.Globalization;
using System.Text.RegularExpressions;
using Stx.Core.Errors;
using Stx.Core.Files;

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
public sealed partial class FfmpegRunner(IProcessRunner process, string ffmpegPath, Func<Task<string>>? h264Encoder = null)
{
    const int LinesKeptForDiagnosis = 200;
    const string ProgressKey = "out_time_ms=";

    public async Task<ConvertResult> ConvertAsync(ConvertRequest request, Action<double>? onProgress = null, CancellationToken cancel = default, Action<string>? onLog = null)
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
        // A short, fixed name, because a long title plus a suffix could pass the path limit before the real name is chosen.
        string partial = Path.Combine(request.OutputFolder, $"stx.partial-{Guid.NewGuid():N}{extension}");
        string encoder = request.Reencode && h264Encoder is not null ? await h264Encoder().ConfigureAwait(false) : "libx264";

        var recent = new Queue<string>();
        var length = new InputLength { Seconds = request.DurationSeconds };
        try
        {
            int exit = await process.RunAsync(ffmpegPath, FfmpegArgs.ForConvert(request, partial, encoder), line => OnLine(line, recent, length, onProgress, onLog), cancel).ConfigureAwait(false);
            if (exit != 0)
            {
                string evidence;
                lock (recent) evidence = string.Join('\n', recent);
                return ConvertResult.Failed(ErrorClassifier.Classify(evidence, exit));
            }

            string target = Filename.Safe(stem, extension, request.OutputFolder, File.Exists);
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

    void OnLine(string line, Queue<string> recent, InputLength length, Action<double>? onProgress, Action<string>? onLog)
    {
        // Nothing has to measure the input first, because ffmpeg says how long it is before it starts.
        if (length.Seconds is null && StatedLength().Match(line) is { Success: true } stated)
            length.Seconds = int.Parse(stated.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                + int.Parse(stated.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                + double.Parse(stated.Groups[3].Value, CultureInfo.InvariantCulture);

        lock (recent)
        {
            recent.Enqueue(line);
            if (recent.Count > LinesKeptForDiagnosis) recent.Dequeue();
        }
        // Its progress reports are many bare key=value lines a second, and say nothing a log reader wants.
        if (!ProgressField().IsMatch(line)) onLog?.Invoke(line);

        // Despite its name, ffmpeg reports out_time_ms in microseconds.
        if (length.Seconds is > 0 && line.StartsWith(ProgressKey, StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(ProgressKey.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros))
            onProgress?.Invoke(Math.Clamp(micros / 1_000_000.0 / length.Seconds.Value * 100, 0, 100));
    }

    sealed class InputLength
    {
        public double? Seconds;
    }

    [GeneratedRegex(@"^[a-z_0-9]+=\S*$")]
    private static partial Regex ProgressField();

    [GeneratedRegex(@"^\s*Duration: (\d+):(\d\d):(\d\d(?:\.\d+)?),")]
    private static partial Regex StatedLength();
}
