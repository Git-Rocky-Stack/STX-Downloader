using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Settings;
using Stx.Look;
using Stx.Tools;

namespace Stx.ViewModels;

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void Set(bool enabled);
}

/// <summary>Settings on the panel. Every change saves at once and tells the rest of the app.</summary>
public sealed class SettingsViewModel(SettingsStore store, IStartupRegistration startup) : ObservableObject
{
    AppSettings _settings = store.Load();

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => _settings;

    public bool LightMode { get => _settings.LookMode == LookMode.Light; set => Apply(_settings with { LookMode = value ? LookMode.Light : LookMode.Dark }); }
    public int MaxParallel { get => _settings.MaxParallel; set => Apply(_settings with { MaxParallel = value }); }
    public bool ClipboardDetect { get => _settings.ClipboardDetect; set => Apply(_settings with { ClipboardDetect = value }); }
    public bool TrayMode { get => _settings.TrayMode; set => Apply(_settings with { TrayMode = value }); }
    public bool ToolUpdates { get => _settings.ToolUpdates; set => Apply(_settings with { ToolUpdates = value }); }
    public bool ReduceMotion { get => _settings.ReduceMotion; set => Apply(_settings with { ReduceMotion = value }); }
    public DownloadChoice Download { get => _settings.Download; set => Apply(_settings with { Download = value }); }
    public Stx.Core.Playback.EnhanceLevel Enhance { get => _settings.Enhance; set => Apply(_settings with { Enhance = value }); }
    public bool BitPerfect { get => _settings.BitPerfect; set => Apply(_settings with { BitPerfect = value }); }
    public bool EmbedTags { get => _settings.EmbedTags; set => Apply(_settings with { EmbedTags = value }); }
    public bool EmbedCoverArt { get => _settings.EmbedCoverArt; set => Apply(_settings with { EmbedCoverArt = value }); }
    public bool EmbedChapters { get => _settings.EmbedChapters; set => Apply(_settings with { EmbedChapters = value }); }

    public Stx.Core.Playback.PlaybackPerformance Performance { get => _settings.Performance; set => Apply(_settings with { Performance = value }); }

    /// <summary>In the order of PlaybackPerformance.</summary>
    public static IReadOnlyList<string> PerformanceLabels { get; } = ["AUTO", "ECO", "BALANCED", "MAX"];

    /// <summary>In the order of DownloadChoice.</summary>
    public static IReadOnlyList<string> DownloadLabels { get; } = ["BEST", "1080P", "720P", "VIDEO", "AUDIO"];
    public string OutputFolder { get => _settings.OutputFolder; set => Apply(_settings with { OutputFolder = value }); }

    /// <summary>The folders whose media joins the Library. Videos and Music until changed.</summary>
    public IReadOnlyList<string> WatchedFolders => _settings.Watched;

    public void AddWatchedFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || _settings.Watched.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        Apply(_settings with { WatchedFolders = [.. _settings.Watched, folder] }, nameof(WatchedFolders));
    }

    /// <summary>Only stops watching. Nothing is deleted from disk.</summary>
    public void RemoveWatchedFolder(string folder) =>
        Apply(_settings with { WatchedFolders = [.. _settings.Watched.Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))] }, nameof(WatchedFolders));
    public string FilenameTemplate { get => _settings.FilenameTemplate; set => Apply(_settings with { FilenameTemplate = value }); }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (value == _settings.StartWithWindows) return;
            startup.Set(value);
            Apply(_settings with { StartWithWindows = value });
        }
    }

    /// <summary>Records that the default-player question was answered, so it's never asked again.</summary>
    public void MarkDefaultPlayerAsked() => Apply(_settings with { DefaultPlayerAsked = true });

    /// <summary>True on the first launch only. Saved straight away, so the guide never opens by itself twice.</summary>
    public bool TakeFirstLaunchGuide()
    {
        if (_settings.GuideShown) return false;
        Apply(_settings with { GuideShown = true });
        return true;
    }

    void Apply(AppSettings next, [CallerMemberName] string? name = null)
    {
        var clean = next.Sanitized();
        if (clean == _settings) return;
        _settings = clean;
        store.Save(clean);
        OnPropertyChanged(name);
        Changed?.Invoke(this, clean);
    }
}

/// <summary>Installing the tools, and saying plainly what to do when that can't finish. The app is usable either way it opens.</summary>
public sealed class FirstRunViewModel : ObservableObject
{
    readonly IToolSetup _setup;
    readonly EngineStatusViewModel _status;
    bool _ready, _visible, _canRetry, _busy;
    string _message = "";

    readonly Func<bool> _downloadsAllowed;

    public FirstRunViewModel(IToolSetup setup, EngineStatusViewModel status, Func<bool>? downloadsAllowed = null)
    {
        _downloadsAllowed = downloadsAllowed ?? (() => true);
        _setup = setup;
        _status = status;
        RetryCommand = new AsyncRelayCommand(() => StartAsync());
    }

    public IAsyncRelayCommand RetryCommand { get; }
    public bool IsReady { get => _ready; private set => SetProperty(ref _ready, value); }

    /// <summary>Tools already on disk can work while their update check runs, so a first click after launch isn't refused.</summary>
    public bool ToolsUsable(bool onDisk) => onDisk || IsReady;
    public bool IsVisible { get => _visible; private set => SetProperty(ref _visible, value); }
    public bool CanRetry { get => _canRetry; private set => SetProperty(ref _canRetry, value); }
    public bool IsBusy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public async Task StartAsync(CancellationToken cancel = default)
    {
        IsBusy = true;
        CanRetry = false;
        var progress = new ThreadSafeProgress(_status.Report);
        try
        {
            ToolHealth health;
            try { health = await _setup.EnsureAsync(progress, cancel); }
            finally { progress.Finish(); }
            _status.Apply(health);
            IsReady = health != ToolHealth.Missing;
            IsVisible = health is ToolHealth.Missing or ToolHealth.Degraded;
            CanRetry = IsVisible;
            Message = health switch
            {
                // With tool updates off, nothing was attempted, so the connection isn't the problem and Retry alone can't help.
                ToolHealth.Missing when !_downloadsAllowed() => "Tool updates are off, so the missing tools weren't downloaded. Turn tool updates on in Settings, then retry.",
                ToolHealth.Missing => "The tools couldn't download. Check your connection, then retry.",
                ToolHealth.Degraded => "Deno couldn't download, so some formats won't be offered. Everything else works.",
                _ => "",
            };
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Setup should report failures itself, but whatever gets out still leaves you a way forward.
            _status.Apply(ToolHealth.Missing);
            IsReady = false;
            IsVisible = true;
            CanRetry = true;
            Message = "The tools couldn't be set up. Check your connection and disk space, then retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Tool setup reports from pool threads, and what it reports drives controls that only accept the UI thread.
    /// A posted report can arrive after setup has ended, and it mustn't replace how setup ended.
    /// </summary>
    sealed class ThreadSafeProgress(Action<ToolStatus> report) : IProgress<ToolStatus>
    {
        readonly SynchronizationContext? _home = SynchronizationContext.Current;
        readonly object _gate = new();
        bool _finished;

        public void Report(ToolStatus value)
        {
            if (_home is null) Deliver(value);
            else _home.Post(_ => Deliver(value), null);
        }

        /// <summary>Once this returns, no report is being delivered and none will be.</summary>
        public void Finish()
        {
            lock (_gate) _finished = true;
        }

        void Deliver(ToolStatus value)
        {
            lock (_gate)
            {
                if (!_finished) report(value);
            }
        }
    }
}

/// <summary>Offers a copied link. Off by default, and nothing is read unless it's turned on.</summary>
public sealed class ClipboardOfferViewModel(Func<bool> enabled) : ObservableObject
{
    string? _offered, _dismissed;

    public string? OfferedLink { get => _offered; private set => SetProperty(ref _offered, value); }

    public void OnClipboardText(string text)
    {
        if (!enabled()) return;
        string candidate = text?.Trim() ?? "";
        if (Stx.Core.Settings.LinkDetector.IsMediaLink(candidate) && candidate != _dismissed) OfferedLink = candidate;
    }

    public void Dismiss()
    {
        _dismissed = OfferedLink;
        OfferedLink = null;
    }

    public string? Take()
    {
        string? link = OfferedLink;
        _dismissed = link;
        OfferedLink = null;
        return link;
    }
}

/// <summary>Stops timers while the window is hidden, so a hidden app does no work. Only real work, such as a pending retry, carries on.</summary>
public sealed class WindowActivity
{
    readonly List<(Func<bool> Wanted, Action Start, Action Stop, bool KeepWhenHidden)> _timers = [];
    bool _visible;

    public void Register(Func<bool> wanted, Action start, Action stop, bool keepRunningWhenHidden) =>
        _timers.Add((wanted, start, stop, keepRunningWhenHidden));

    public void SetVisible(bool visible)
    {
        _visible = visible;
        Refresh();
    }

    public void Refresh()
    {
        foreach (var (wanted, start, stop, keep) in _timers)
        {
            if (wanted() && (_visible || keep)) start();
            else stop();
        }
    }
}
