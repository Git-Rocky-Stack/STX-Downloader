using Stx.Core.Audio;

namespace Stx.ViewModels;

/// <summary>What a conversion job should do. It's saved with the job, so a conversion survives a restart like anything else.</summary>
public sealed record ConversionPlan(OutputChoice Output, long FileId, string? PresetName)
{
    const string ConvertPrefix = "convert:";
    const string ThenPrefix = "then:";

    public static ConversionPlan? Parse(string? text)
    {
        if (text is null || !text.StartsWith(ConvertPrefix, StringComparison.Ordinal)) return null;
        string[] parts = text[ConvertPrefix.Length..].Split(':', 3);
        if (parts.Length < 2 || !Enum.TryParse<OutputChoice>(parts[0], out var output) || !long.TryParse(parts[1], out long file)) return null;
        return new ConversionPlan(output, file, parts.Length == 3 ? parts[2] : null);
    }

    public override string ToString() =>
        PresetName is null ? $"{ConvertPrefix}{Output}:{FileId}" : $"{ConvertPrefix}{Output}:{FileId}:{PresetName}";

    /// <summary>The note on a download that a format was chosen for it in advance.</summary>
    public static string Then(OutputChoice output) => ThenPrefix + output;

    public static OutputChoice? AfterDownload(string? preset) =>
        preset is not null && preset.StartsWith(ThenPrefix, StringComparison.Ordinal) && Enum.TryParse<OutputChoice>(preset[ThenPrefix.Length..], out var output) ? output : null;
}
