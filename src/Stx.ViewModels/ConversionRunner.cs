using Stx.Core.Errors;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;

namespace Stx.ViewModels;

/// <summary>Runs a conversion job: finds its file, converts beside it, and records the result as another file of that item.</summary>
public sealed class ConversionRunner(FfmpegRunner ffmpeg, Func<long, string?> filePath, Func<long, string, string, long> addFile) : IJobRunner
{
    public event EventHandler<SavedMedia>? FileSaved;

    public async Task<JobOutcome> RunAsync(Job job, CancellationToken cancel)
    {
        if (ConversionPlan.Parse(job.Preset) is not { } plan || filePath(plan.FileId) is not { } input)
            return JobOutcome.Failure(ErrorKind.ToolFailure);

        var preset = PresetCatalog.All.FirstOrDefault(p => p.Name == plan.PresetName);
        var request = new ConvertRequest(input, Path.GetDirectoryName(input) ?? "", plan.Output)
        {
            Reencode = preset?.Reencodes == true,
            MaxHeight = preset?.MaxHeight,
            Metadata = preset?.Metadata ?? new MetadataOptions(true, false, true),
        };

        var result = await ffmpeg.ConvertAsync(request, null, cancel).ConfigureAwait(false);
        if (!result.IsSuccess) return JobOutcome.Failure(result.Error ?? ErrorKind.ToolFailure);

        long fileId = addFile(job.MediaId, result.OutputPath!, plan.PresetName ?? plan.Output.ToString());
        FileSaved?.Invoke(this, new SavedMedia(job.MediaId, fileId, result.OutputPath!, Path.GetFileNameWithoutExtension(result.OutputPath!)));
        return JobOutcome.Success;
    }
}

/// <summary>Sends each job to the runner for its kind.</summary>
public sealed class JobRouter(IJobRunner downloads, IJobRunner conversions) : IJobRunner
{
    public Task<JobOutcome> RunAsync(Job job, CancellationToken cancel) =>
        (job.Kind == JobKind.Convert ? conversions : downloads).RunAsync(job, cancel);
}
