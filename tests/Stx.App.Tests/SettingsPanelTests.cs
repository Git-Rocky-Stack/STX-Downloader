using Stx.Core.Queue;
using Stx.Core.Settings;
using Stx.Look;
using Stx.Tools;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

sealed class FakeStartup : IStartupRegistration
{
    public bool Enabled;
    public bool IsEnabled => Enabled;
    public void Set(bool enabled) => Enabled = enabled;
}

public sealed class SettingsPanelTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("stx-vm").FullName;
    readonly FakeStartup _startup = new();

    public void Dispose() => Directory.Delete(_dir, true);

    SettingsStore Store() => new(Path.Combine(_dir, "s.json"));

    SettingsViewModel Panel() => new(Store(), _startup);

    [Fact]
    public void Changing_a_setting_saves_it()
    {
        var panel = Panel();
        panel.MaxParallel = 5;
        panel.ClipboardDetect = true;
        var reloaded = Store().Load();
        Assert.Equal(5, reloaded.MaxParallel);
        Assert.True(reloaded.ClipboardDetect);
    }

    [Fact]
    public void Changing_a_setting_announces_it()
    {
        var panel = Panel();
        AppSettings? seen = null;
        panel.Changed += (_, s) => seen = s;
        panel.ToolUpdates = false;
        Assert.False(seen!.ToolUpdates);
    }

    [Fact]
    public void Setting_the_same_value_writes_nothing()
    {
        var panel = Panel();
        int changes = 0;
        panel.Changed += (_, _) => changes++;
        panel.MaxParallel = panel.MaxParallel;
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Light_mode_switch_changes_the_mode()
    {
        var panel = Panel();
        Assert.False(panel.LightMode);
        panel.LightMode = true;
        Assert.Equal(LookMode.Light, Store().Load().LookMode);
    }

    [Fact]
    public void Start_with_windows_reaches_the_registry_only_when_changed()
    {
        var panel = Panel();
        Assert.False(_startup.Enabled);
        panel.StartWithWindows = true;
        Assert.True(_startup.Enabled);
        panel.StartWithWindows = false;
        Assert.False(_startup.Enabled);
    }

    [Fact]
    public void Parallel_cannot_be_set_out_of_range()
    {
        var panel = Panel();
        panel.MaxParallel = 0;
        Assert.Equal(1, panel.MaxParallel);
        panel.MaxParallel = 99;
        Assert.Equal(AppSettings.MostParallel, panel.MaxParallel);
    }

    [Fact]
    public void A_blank_output_folder_is_refused()
    {
        var panel = Panel();
        string before = panel.OutputFolder;
        panel.OutputFolder = "   ";
        Assert.Equal(before, panel.OutputFolder);
    }
}

sealed class FakeSetup(params ToolHealth[] answers) : IToolSetup
{
    int _at;
    public int Calls;

    public Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default)
    {
        Calls++;
        progress?.Report(new ToolStatus("FFmpeg", "Downloading"));
        return Task.FromResult(answers[Math.Min(_at++, answers.Length - 1)]);
    }
}

public class FirstRunTests
{
    [Fact]
    public void Tools_already_on_disk_are_usable_while_the_update_check_is_still_going()
    {
        var panel = new FirstRunViewModel(new FakeSetup(ToolHealth.Current), new EngineStatusViewModel());
        Assert.False(panel.IsReady);
        Assert.True(panel.ToolsUsable(onDisk: true));
    }

    [Fact]
    public void Tools_not_on_disk_are_not_usable_until_setup_finishes()
    {
        var panel = new FirstRunViewModel(new FakeSetup(ToolHealth.Current), new EngineStatusViewModel());
        Assert.False(panel.ToolsUsable(onDisk: false));
    }

    [Fact]
    public async Task Tools_are_usable_once_setup_finishes_even_if_a_path_check_says_otherwise()
    {
        var panel = new FirstRunViewModel(new FakeSetup(ToolHealth.Current), new EngineStatusViewModel());
        await panel.StartAsync();
        Assert.True(panel.ToolsUsable(onDisk: false));
    }

    static FirstRunViewModel Panel(params ToolHealth[] answers) => new(new FakeSetup(answers), new EngineStatusViewModel());

    [Fact]
    public async Task First_run_offline_shows_retry()
    {
        var panel = Panel(ToolHealth.Missing);
        await panel.StartAsync();
        Assert.True(panel.IsVisible);
        Assert.True(panel.CanRetry);
        Assert.Contains("download", panel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(panel.IsReady);
    }

    [Fact]
    public async Task Retry_tries_again_and_clears_when_it_works()
    {
        var setup = new FakeSetup(ToolHealth.Missing, ToolHealth.Current);
        var panel = new FirstRunViewModel(setup, new EngineStatusViewModel());
        await panel.StartAsync();
        await panel.RetryCommand.ExecuteAsync(null);
        Assert.Equal(2, setup.Calls);
        Assert.True(panel.IsReady);
        Assert.False(panel.IsVisible);
    }

    [Fact]
    public async Task A_second_failure_keeps_the_retry()
    {
        var panel = Panel(ToolHealth.Missing, ToolHealth.Missing);
        await panel.StartAsync();
        await panel.RetryCommand.ExecuteAsync(null);
        Assert.True(panel.CanRetry);
    }

    [Theory]
    [InlineData(ToolHealth.Current)]
    [InlineData(ToolHealth.UpdatePending)]
    public async Task With_tools_ready_there_is_nothing_to_show(ToolHealth health)
    {
        var panel = Panel(health);
        await panel.StartAsync();
        Assert.False(panel.IsVisible);
        Assert.True(panel.IsReady);
    }

    [Fact]
    public async Task Missing_deno_explains_itself_but_the_app_still_works()
    {
        var panel = Panel(ToolHealth.Degraded);
        await panel.StartAsync();
        Assert.True(panel.IsReady);
        Assert.True(panel.IsVisible);
        Assert.True(panel.CanRetry);
        Assert.Contains("format", panel.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Setup_progress_reaches_the_engine_line()
    {
        var status = new EngineStatusViewModel();
        var panel = new FirstRunViewModel(new FakeSetup(ToolHealth.Current), status);
        await panel.StartAsync();
        Assert.Equal("ENGINE CURRENT", status.EngineText);
    }

    // Reports are posted to the UI thread, so one can be delivered after setup has already said how it ended.
    [Fact]
    public void A_report_delivered_after_setup_ended_does_not_replace_the_result()
    {
        var home = new HeldPosts();
        var status = new EngineStatusViewModel();
        var outer = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(home);
        try
        {
            var run = new FirstRunViewModel(new FakeSetup(ToolHealth.Current), status).StartAsync();
            Assert.True(run.IsCompletedSuccessfully);
            Assert.Equal(1, home.Waiting);
            home.Deliver();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(outer);
        }
        Assert.Equal("ENGINE CURRENT", status.EngineText);
    }

    [Fact]
    public void While_setup_runs_its_reports_reach_the_engine_line()
    {
        var home = new HeldPosts();
        var setup = new ReportsThenWaits();
        var status = new EngineStatusViewModel();
        var outer = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(home);
        try
        {
            var run = new FirstRunViewModel(setup, status).StartAsync();
            home.Deliver();
            Assert.Equal("FFMPEG: DOWNLOADING", status.EngineText);

            setup.Finish(ToolHealth.Current);
            home.Deliver();
            Assert.True(run.IsCompletedSuccessfully);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(outer);
        }
        Assert.Equal("ENGINE CURRENT", status.EngineText);
    }

    /// <summary>Stands in for the UI thread: nothing posted runs until the test says so.</summary>
    sealed class HeldPosts : SynchronizationContext
    {
        readonly Queue<(SendOrPostCallback Callback, object? State)> _posts = new();
        public int Waiting => _posts.Count;
        public override void Post(SendOrPostCallback d, object? state) => _posts.Enqueue((d, state));
        public void Deliver()
        {
            while (_posts.TryDequeue(out var post)) post.Callback(post.State);
        }
    }

    sealed class ReportsThenWaits : IToolSetup
    {
        readonly TaskCompletionSource<ToolHealth> _end = new();
        public void Finish(ToolHealth health) => _end.SetResult(health);

        public Task<ToolHealth> EnsureAsync(IProgress<ToolStatus>? progress = null, CancellationToken cancel = default)
        {
            progress?.Report(new ToolStatus("FFmpeg", "Downloading"));
            return _end.Task;
        }
    }

    [Fact]
    public void Before_setup_finishes_it_is_not_ready() =>
        Assert.False(Panel(ToolHealth.Current).IsReady);
}

public class AnalyzeBeforeReadyTests : ConsoleFixture
{
    [Fact]
    public async Task A_link_pasted_during_setup_says_so_and_keeps_the_link()
    {
        var analyzer = new ScriptedAnalyzer(_ => One());
        var source = Source(analyzer);
        source.ToolsReady = false;
        source.Link = "https://x/abc";

        await source.AnalyzeCommand.ExecuteAsync(null);

        Assert.Empty(analyzer.Asked);
        Assert.Contains("setting up", source.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://x/abc", source.Link);
    }
}

public class ClipboardTests
{
    [Fact]
    public void Clipboard_off_makes_no_offer()
    {
        var offer = new ClipboardOfferViewModel(() => false);
        offer.OnClipboardText("https://site.example/abc");
        Assert.Null(offer.OfferedLink);
    }

    [Fact]
    public void Clipboard_on_offers_a_copied_link()
    {
        var offer = new ClipboardOfferViewModel(() => true);
        offer.OnClipboardText("https://site.example/abc");
        Assert.Equal("https://site.example/abc", offer.OfferedLink);
    }

    [Fact]
    public void Plain_text_makes_no_offer()
    {
        var offer = new ClipboardOfferViewModel(() => true);
        offer.OnClipboardText("meeting notes");
        Assert.Null(offer.OfferedLink);
    }

    [Fact]
    public void A_dismissed_link_is_not_offered_again()
    {
        var offer = new ClipboardOfferViewModel(() => true);
        offer.OnClipboardText("https://site.example/abc");
        offer.Dismiss();
        offer.OnClipboardText("https://site.example/abc");
        Assert.Null(offer.OfferedLink);
    }

    [Fact]
    public void A_new_link_gets_its_own_offer()
    {
        var offer = new ClipboardOfferViewModel(() => true);
        offer.OnClipboardText("https://site.example/1");
        offer.Dismiss();
        offer.OnClipboardText("https://site.example/2");
        Assert.Equal("https://site.example/2", offer.OfferedLink);
    }

    [Fact]
    public void Taking_an_offer_returns_the_link_and_clears_it()
    {
        var offer = new ClipboardOfferViewModel(() => true);
        offer.OnClipboardText("  https://site.example/abc ");
        Assert.Equal("https://site.example/abc", offer.Take());
        Assert.Null(offer.OfferedLink);
    }
}

public class HiddenStateTests
{
    sealed class Timer
    {
        public bool Running;
    }

    static (WindowActivity Activity, Timer Timer) One(Func<bool>? wanted = null)
    {
        var activity = new WindowActivity();
        var timer = new Timer();
        activity.Register(wanted ?? (() => true), () => timer.Running = true, () => timer.Running = false, keepRunningWhenHidden: false);
        return (activity, timer);
    }

    [Fact]
    public void Hidden_state_stops_all_timers()
    {
        var activity = new WindowActivity();
        var timers = new[] { new Timer(), new Timer(), new Timer() };
        foreach (var t in timers)
            activity.Register(() => true, () => t.Running = true, () => t.Running = false, keepRunningWhenHidden: false);
        activity.SetVisible(true);
        Assert.All(timers, t => Assert.True(t.Running));

        activity.SetVisible(false);

        Assert.All(timers, t => Assert.False(t.Running));
    }

    [Fact]
    public void Showing_again_restarts_only_what_is_still_wanted()
    {
        bool wanted = true;
        var (activity, timer) = One(() => wanted);
        activity.SetVisible(true);
        activity.SetVisible(false);
        wanted = false;
        activity.SetVisible(true);
        Assert.False(timer.Running);
    }

    [Fact]
    public void A_retry_timer_keeps_going_while_hidden_because_that_is_real_work()
    {
        var activity = new WindowActivity();
        var retry = new Timer();
        activity.Register(() => true, () => retry.Running = true, () => retry.Running = false, keepRunningWhenHidden: true);
        activity.SetVisible(true);
        activity.SetVisible(false);
        Assert.True(retry.Running);
    }

    [Fact]
    public void A_retry_timer_stops_when_nothing_is_waiting_even_when_visible()
    {
        bool waiting = true;
        var activity = new WindowActivity();
        var retry = new Timer();
        activity.Register(() => waiting, () => retry.Running = true, () => retry.Running = false, keepRunningWhenHidden: true);
        activity.SetVisible(true);
        waiting = false;
        activity.Refresh();
        Assert.False(retry.Running);
    }
}
