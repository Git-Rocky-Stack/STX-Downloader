using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stx.Bridge;

/// <summary>One stream in a batch.</summary>
public sealed record CaptureItem
{
    public string? Kind { get; init; }
    public string? Title { get; init; }
    public string? StreamUrl { get; init; }
    public string? VideoId { get; init; }
    public double? DurationSec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool IsDrm { get; init; }

    public override string ToString() => "CaptureItem([redacted])";
}

/// <summary>What the extension sends when you click. It carries live secrets, so it refuses to print itself.</summary>
public sealed record CaptureMessage
{
    public string? Kind { get; init; }
    public string? PageUrl { get; init; }
    public string? Title { get; init; }
    public string? StreamUrl { get; init; }
    public string? VideoId { get; init; }
    public double? DurationSec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? Cookies { get; init; }
    public bool IsDrm { get; init; }
    public CaptureItem[]? Items { get; init; }

    public static JsonSerializerOptions Options { get; } = new(BridgeJson.Default.Options) { PropertyNameCaseInsensitive = true };

    public override string ToString() => "CaptureMessage([redacted])";
}

[JsonSerializable(typeof(CaptureMessage))]
internal sealed partial class BridgeJson : JsonSerializerContext;
