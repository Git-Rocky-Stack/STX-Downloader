using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stx.Core.Errors;
using Stx.Core.Jobs;
using Stx.Core.Logging;
using Stx.Core.Presets;
using Stx.Core.Queue;

namespace Stx.Bridge;

/// <summary>Turns a click in the extension into queue jobs. Everything arriving here is checked first, and a bad message makes no jobs at all.</summary>
public sealed partial class CaptureHandler(IMediaCatalog catalog, QueueEngine engine, Func<bool> toolsReady, Func<string, bool>? fileExists = null)
{
    public const int MaxBatch = 200;
    const int MaxTitle = 300;

    static readonly string[] AllowedHeaders = ["Referer", "Origin", "User-Agent"];

    readonly object _handling = new();
    readonly Func<string, bool> _fileExists = fileExists ?? File.Exists;

    sealed record Planned(string PageUrl, string? SourceId, string? Title, CaptureSecrets? Secrets);

    public string Handle(string json)
    {
        CaptureMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<CaptureMessage>(json, CaptureMessage.Options);
        }
        catch (JsonException)
        {
            // JsonException's text can quote the input, so none of it goes any further.
            return Malformed();
        }
        if (message is null || !IsWebLink(message.PageUrl)) return Malformed();

        var sources = message.Items is { } items
            ? items.Select(i => new CaptureItem { Kind = i.Kind, Title = i.Title, StreamUrl = i.StreamUrl, VideoId = i.VideoId, IsDrm = i.IsDrm }).ToArray()
            : [new CaptureItem { Kind = message.Kind, Title = message.Title, StreamUrl = message.StreamUrl, VideoId = message.VideoId, IsDrm = message.IsDrm }];

        if (sources.Length > MaxBatch) return BridgeReply.Refused("TooMany", $"That's more than {MaxBatch} items at once.");
        if (message.IsDrm || sources.All(s => s.IsDrm)) return BridgeReply.Refused(nameof(ErrorKind.DrmDetected), ErrorKind.DrmDetected.Message());

        var headers = KeepAllowed(message.Headers);
        var plans = new List<Planned>();
        foreach (var source in sources.Where(s => !s.IsDrm))
        {
            if (Plan(source, message, headers) is not { } plan) return Malformed();
            plans.Add(plan);
        }
        if (!toolsReady()) return BridgeReply.Refused("NotReady", "STX Downloader is still setting up. Try again when it's ready.");

        int resumed = 0, alreadyHere = 0;
        lock (_handling)
        {
            // Decided once, before this click adds anything, so two rows of one click never count against each other.
            var present = MediaAlreadyHere();
            foreach (var plan in plans)
            {
                if (plan.Secrets is { } secrets && Resume(plan, secrets))
                {
                    resumed++;
                    continue;
                }
                string key = StreamKey(plan);
                if (catalog.RememberedCapture(key) is { } earlier && present.Contains(earlier))
                {
                    alreadyHere++;
                    continue;
                }
                long media = catalog.AddItem(plan.PageUrl, plan.SourceId, plan.Title);
                engine.Enqueue(media, plan.Secrets is null ? JobKind.Link : JobKind.Captured, PresetCatalog.Default.Name, plan.Secrets);
                catalog.RememberCapture(key, media);
            }
        }
        return BridgeReply.Sent(plans.Count - resumed - alreadyHere, resumed, alreadyHere);
    }

    // In the queue and still going, or finished with its file still on disk. Failed, canceled or deleted work can be sent again.
    HashSet<long> MediaAlreadyHere()
    {
        var present = engine.AllJobs()
            .Where(j => j.State is not (JobState.Failed or JobState.Canceled or JobState.Completed))
            .Select(j => j.MediaId)
            .ToHashSet();
        foreach (var fact in catalog.ExistingFacts())
            if (fact.Path is { } path && _fileExists(path)) present.Add(fact.MediaId);
        return present;
    }

    // Only secrets are ignored, because a fresh token on a stream is the same stream, while ?video=42 and ?video=43 are not.
    // A token under a name nobody knows just makes this miss, and then the bytes are compared once the file exists.
    // It's stored, so only its hash leaves here: an unknown token mustn't reach the database.
    static string StreamKey(Planned plan) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            plan.SourceId is { } id ? "id:" + id : $"stream:{Redactor.ScrubLink(plan.Secrets!.StreamUrl)}\n{plan.Title}")));

    static Planned? Plan(CaptureItem source, CaptureMessage message, IReadOnlyDictionary<string, string> headers)
    {
        string? title = source.Title ?? message.Title;
        if (title is { Length: > MaxTitle }) title = title[..MaxTitle];

        if (source.Kind == "youtube")
        {
            if (source.VideoId is not { } id || !YouTubeId().IsMatch(id)) return null;
            string watch = $"https://www.youtube.com/watch?v={id}";
            // Cookies make it a captured job, because only those keep their secrets out of the database.
            // Save all never sends your YouTube sign-in. Only the button that says so on one video does.
            bool signedIn = message.Items is null && !string.IsNullOrEmpty(message.Cookies);
            var secrets = signedIn ? new CaptureSecrets(watch, headers, message.Cookies) : null;
            return new Planned(watch, id, title ?? id, secrets);
        }

        if (source.Kind is not ("hls" or "dash" or "file") || !IsWebLink(source.StreamUrl)) return null;
        return new Planned(message.PageUrl!, null, title, new CaptureSecrets(source.StreamUrl!, headers, message.Cookies));
    }

    bool Resume(Planned plan, CaptureSecrets secrets)
    {
        foreach (var job in engine.AllJobs().Where(j => j is { State: JobState.NeedsRecapture, Kind: JobKind.Captured }))
        {
            var item = catalog.GetItem(job.MediaId);
            // Stored links have their tokens scrubbed, and a new capture's token will differ anyway.
            if (item?.PageUrl == Redactor.ScrubLink(plan.PageUrl) && item.Title == plan.Title) return engine.Recapture(job.Id, secrets);
        }
        return false;
    }

    static Dictionary<string, string> KeepAllowed(Dictionary<string, string>? headers)
    {
        var kept = new Dictionary<string, string>();
        if (headers is null) return kept;
        foreach (var (name, value) in headers)
        {
            string? allowed = AllowedHeaders.FirstOrDefault(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (allowed is not null) kept[allowed] = value;
        }
        return kept;
    }

    static bool IsWebLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

    static string Malformed() => BridgeReply.Refused("Malformed", "The extension sent something the app couldn't read.");

    [GeneratedRegex(@"^[\w-]{11}$")]
    private static partial Regex YouTubeId();
}
