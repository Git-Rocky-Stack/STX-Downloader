using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Audio;
using Stx.Core.Audio;
using Stx.Core.Errors;
using Stx.Core.Logging;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;

namespace Stx.ViewModels;

public sealed record PlaylistOffer(string? Title, int Available, int Unavailable, IReadOnlyList<PlaylistEntry> Entries, string PageUrl)
{
    /// <summary>The one video this link points at, when it also names a playlist.</summary>
    public string? SingleVideoUrl => SingleVideoLink.From(PageUrl);
}

/// <summary>A YouTube link can name both a video and a playlist. This is the video alone.</summary>
public static class SingleVideoLink
{
    public static string? From(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Host is not ("www.youtube.com" or "youtube.com" or "m.youtube.com") || uri.AbsolutePath != "/watch") return null;
        string? video = uri.Query.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("v=", StringComparison.Ordinal));
        return video is { Length: > 2 } ? $"https://www.youtube.com/watch?{video}" : null;
    }
}

/// <summary>The paste box: reads a link, explains any problem with a next step, and fills the queue.</summary>
public sealed class SourceViewModel : ObservableObject
{
    /// <summary>How a kept-original file is recorded, so a later conversion of the same source isn't called a duplicate.</summary>
    public const string OriginalFormat = "Original";

    readonly ILinkAnalyzer _analyzer;
    readonly IMediaCatalog _catalog;
    readonly QueueEngine _engine;
    readonly Func<OutputChoice> _chosenOutput;
    readonly Func<string, bool> _fileExists;
    AudioFacts? _reported;
    string _link = "", _message = "", _nextStep = "";
    bool _busy;
    PlaylistOffer? _pending;

    public SourceViewModel(ILinkAnalyzer analyzer, IMediaCatalog catalog, QueueEngine engine, Func<OutputChoice>? chosenOutput = null, Func<string, bool>? fileExists = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _chosenOutput = chosenOutput ?? (() => OutputChoice.Original);
        _analyzer = analyzer;
        _catalog = catalog;
        _engine = engine;
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        ConfirmPlaylistCommand = new RelayCommand(ConfirmPlaylist);
        DeclinePlaylistCommand = new RelayCommand(() => PendingPlaylist = null);
        JustThisVideoCommand = new AsyncRelayCommand(JustThisVideoAsync);
    }

    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IRelayCommand ConfirmPlaylistCommand { get; }
    public IRelayCommand DeclinePlaylistCommand { get; }
    public IAsyncRelayCommand JustThisVideoCommand { get; }

    /// <summary>False while the tools are still installing, when reading a link would only fail.</summary>
    public bool ToolsReady { get; set; } = true;

    public string Link { get => _link; set => SetProperty(ref _link, value ?? ""); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string NextStep { get => _nextStep; private set => SetProperty(ref _nextStep, value); }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    /// <summary>What the site says it has, before anything is downloaded.</summary>
    public AudioFacts? ReportedAudio { get => _reported; private set => SetProperty(ref _reported, value); }

    public PlaylistOffer? PendingPlaylist { get => _pending; private set => SetProperty(ref _pending, value); }

    async Task AnalyzeAsync()
    {
        string url = Link.Trim();
        Say("", "");
        PendingPlaylist = null;

        if (url.Length == 0)
        {
            Say("Paste a link first.", "");
            return;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            Say("That isn't a web link.", "Links start with http:// or https://.");
            return;
        }
        if (!ToolsReady)
        {
            Say("Still setting up the tools.", "Your link is kept. Press Analyze again when the engine light is steady.");
            return;
        }

        IsBusy = true;
        try
        {
            var analysis = await _analyzer.AnalyzeAsync(url);
            if (analysis.Error is { } kind) Say(kind.Message(), kind.NextStep());
            else Accept(analysis, url);
        }
        finally
        {
            IsBusy = false;
        }
    }

    void Accept(LinkAnalysis analysis, string url)
    {
        ReportedAudio = analysis.BestAudio is { } best ? ReportedFacts.From(best) : null;
        if (analysis.Entries.Count > 1)
        {
            PendingPlaylist = new PlaylistOffer(analysis.Title, analysis.Entries.Count(e => e.Available), analysis.Entries.Count(e => !e.Available), analysis.Entries, url);
            return;
        }

        var entry = analysis.Entries[0];
        if (!entry.Available)
        {
            Say(ErrorKind.PrivateOrUnavailable.Message(), ErrorKind.PrivateOrUnavailable.NextStep());
            return;
        }
        if (IsDuplicate(entry))
        {
            Say("This is already saved in that format.", "");
            return;
        }

        Enqueue(url, entry);
        Link = "";
    }

    async Task JustThisVideoAsync()
    {
        if (PendingPlaylist?.SingleVideoUrl is not { } url) return;
        Link = url;
        await AnalyzeAsync();
    }

    void ConfirmPlaylist()
    {
        if (PendingPlaylist is not { } offer) return;
        int skipped = 0;
        foreach (var entry in offer.Entries.Where(e => e.Available))
        {
            if (IsDuplicate(entry)) skipped++;
            else Enqueue(entry.Url ?? offer.PageUrl, entry);
        }
        PendingPlaylist = null;
        Link = "";
        Say(skipped > 0 ? $"{skipped} already saved, so skipped." : "", "");
    }

    // A file you've since deleted doesn't count as saved.
    bool IsDuplicate(PlaylistEntry entry)
    {
        var output = _chosenOutput();
        string format = output == OutputChoice.Original ? OriginalFormat : output.ToString();
        var saved = _catalog.ExistingFacts().Where(f => f.Path is null || _fileExists(f.Path));
        return Dedupe.Decide(entry.Id, null, format, saved) == DedupeDecision.Duplicate;
    }

    void Enqueue(string url, PlaylistEntry entry)
    {
        long media = _catalog.AddItem(url, entry.Id, entry.Title);
        var output = _chosenOutput();
        string preset = output == OutputChoice.Original ? PresetCatalog.Default.Name : ConversionPlan.Then(output);
        // The catalog stores links without their tokens, so a link that needs one has to travel in memory, as a capture does.
        if (Redactor.ScrubLink(url) != url) _engine.Enqueue(media, JobKind.Captured, preset, new CaptureSecrets(url, new Dictionary<string, string>(), null));
        else _engine.Enqueue(media, JobKind.Link, preset);
    }

    /// <summary>A plain line on the Source plate from elsewhere, such as why a pasted stream can't play. Empty clears it.</summary>
    public void Report(string message) => Say(message, "");

    void Say(string message, string next)
    {
        Message = message;
        NextStep = next;
    }
}
