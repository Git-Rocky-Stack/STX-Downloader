using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Errors;
using Stx.Core.Presets;
using Stx.Core.Queue;
using Stx.Tools;

namespace Stx.ViewModels;

public sealed record PlaylistOffer(string? Title, int Available, int Unavailable, IReadOnlyList<PlaylistEntry> Entries, string PageUrl);

/// <summary>The paste box: reads a link, explains any problem with a next step, and fills the queue.</summary>
public sealed class SourceViewModel : ObservableObject
{
    /// <summary>How a kept-original file is recorded, so a later conversion of the same source isn't called a duplicate.</summary>
    public const string OriginalFormat = "Original";

    readonly ILinkAnalyzer _analyzer;
    readonly IMediaCatalog _catalog;
    readonly QueueEngine _engine;
    string _link = "", _message = "", _nextStep = "";
    bool _busy;
    PlaylistOffer? _pending;

    public SourceViewModel(ILinkAnalyzer analyzer, IMediaCatalog catalog, QueueEngine engine)
    {
        _analyzer = analyzer;
        _catalog = catalog;
        _engine = engine;
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync);
        ConfirmPlaylistCommand = new RelayCommand(ConfirmPlaylist);
        DeclinePlaylistCommand = new RelayCommand(() => PendingPlaylist = null);
    }

    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IRelayCommand ConfirmPlaylistCommand { get; }
    public IRelayCommand DeclinePlaylistCommand { get; }

    public string Link { get => _link; set => SetProperty(ref _link, value ?? ""); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string NextStep { get => _nextStep; private set => SetProperty(ref _nextStep, value); }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
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

    bool IsDuplicate(PlaylistEntry entry) =>
        Dedupe.Decide(entry.Id, null, OriginalFormat, _catalog.ExistingFacts()) == DedupeDecision.Duplicate;

    void Enqueue(string url, PlaylistEntry entry)
    {
        long media = _catalog.AddItem(url, entry.Id, entry.Title);
        _engine.Enqueue(media, JobKind.Link, PresetCatalog.Default.Name);
    }

    void Say(string message, string next)
    {
        Message = message;
        NextStep = next;
    }
}
