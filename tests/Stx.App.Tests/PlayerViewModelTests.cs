using Stx.Core.Playback;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

/// <summary>Stands in for LibVLC: records what the player asked for, and lets a test set what the engine reports.</summary>
sealed class FakeEngine : IPlaybackEngine
{
    public List<string> Calls { get; } = [];
    public PlaybackState State { get; set; }
    public TimeSpan Position { get; set; }
    public TimeSpan Length { get; set; }
    public bool HasVideo { get; set; } = true;
    public string? Error { get; set; }
    public int Volume { get; set; } = 100;
    public IReadOnlyList<MediaTrack> AudioTracks { get; set; } = [];
    public IReadOnlyList<MediaTrack> SubtitleTracks { get; set; } = [];
    public IReadOnlyList<MediaTrack> Chapters { get; set; } = [];
    public event EventHandler? Changed;

    public void Open(string path) { Calls.Add("open " + path); State = PlaybackState.Playing; }
    public void TogglePause() => Calls.Add("pause");
    public void Stop() { Calls.Add("stop"); State = PlaybackState.Idle; }
    public void Seek(TimeSpan to) { Calls.Add($"seek {to.TotalSeconds:0.##}"); Position = to; }
    public void SelectAudio(int id) => Calls.Add("audio " + id);
    public void SelectSubtitle(int id) => Calls.Add("subtitle " + id);
    public void SelectChapter(int id) => Calls.Add("chapter " + id);
    public void Dispose() => Calls.Add("dispose");
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

public class PlayerViewModelTests
{
    static readonly (string Path, string Title)[] List = [(@"D:\a.mkv", "A"), (@"D:\b.mkv", "B"), (@"D:\c.mkv", "C")];

    static (PlayerViewModel Player, FakeEngine Engine) Make()
    {
        var engine = new FakeEngine();
        return (new PlayerViewModel(engine, run => run()), engine);
    }

    [Fact]
    public void Play_opens_the_path_and_shows_its_title()
    {
        var (player, engine) = Make();
        player.Play(@"D:\b.mkv", "B", List);
        Assert.Equal([@"open D:\b.mkv"], engine.Calls);
        Assert.Equal("B", player.Title);
        Assert.True(player.HasMedia);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void Playing_again_replaces_the_current_media()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        player.Play(@"D:\c.mkv", "C", List);
        Assert.Equal([@"open D:\a.mkv", @"open D:\c.mkv"], engine.Calls);
        Assert.Equal("C", player.Title);
    }

    [Fact]
    public void TimeText_reads_hours_minutes_seconds()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        engine.Position = new TimeSpan(1, 2, 3);
        engine.Raise();
        Assert.Equal("01:02:03", player.TimeText);
    }

    [Fact]
    public void Progress_is_position_over_length()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        engine.Position = TimeSpan.FromSeconds(30);
        engine.Length = TimeSpan.FromSeconds(120);
        engine.Raise();
        Assert.Equal(0.25, player.Progress, 6);
        engine.Length = TimeSpan.Zero;
        engine.Raise();
        Assert.Equal(0, player.Progress);
    }

    [Fact]
    public void Previous_restarts_after_three_seconds()
    {
        var (player, engine) = Make();
        player.Play(@"D:\b.mkv", "B", List);
        engine.Position = TimeSpan.FromSeconds(5);
        player.PreviousCommand.Execute(null);
        Assert.Equal("seek 0", engine.Calls[^1]);
        engine.Position = TimeSpan.FromSeconds(2);
        player.PreviousCommand.Execute(null);
        Assert.Equal(@"open D:\a.mkv", engine.Calls[^1]);
        Assert.Equal("A", player.Title);
    }

    [Fact]
    public void Next_moves_down_the_list_and_stops_after_the_last()
    {
        var (player, engine) = Make();
        player.Play(@"D:\b.mkv", "B", List);
        player.NextCommand.Execute(null);
        Assert.Equal(@"open D:\c.mkv", engine.Calls[^1]);
        player.NextCommand.Execute(null);
        Assert.Equal("stop", engine.Calls[^1]);
    }

    [Theory]
    [InlineData(0.5, 60)]
    [InlineData(1.7, 120)]
    [InlineData(-0.2, 0)]
    public void SeekCommand_maps_fraction_to_time(double fraction, double seconds)
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        engine.Length = TimeSpan.FromSeconds(120);
        player.SeekCommand.Execute(fraction);
        Assert.Equal($"seek {seconds}", engine.Calls[^1]);
    }

    [Fact]
    public void Engine_failure_shows_a_problem()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        Assert.Equal("", player.Problem);
        engine.State = PlaybackState.Failed;
        engine.Raise();
        Assert.Equal("THIS FILE COULDN'T BE PLAYED.", player.Problem);
        Assert.False(player.IsPlaying);
    }

    [Fact]
    public void The_engine_s_own_message_is_shown_as_it_is()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        engine.State = PlaybackState.Failed;
        engine.Error = "DIRAC VIDEO CAN'T BE PLAYED SAFELY";
        engine.Raise();
        Assert.Equal("DIRAC VIDEO CAN'T BE PLAYED SAFELY", player.Problem);
    }

    [Fact]
    public void Audio_only_media_is_marked_as_such()
    {
        var (player, engine) = Make();
        engine.HasVideo = false;
        player.Play(@"D:\song.flac", "Song", [(@"D:\song.flac", "Song")]);
        Assert.True(player.IsAudioOnly);
        engine.HasVideo = true;
        engine.Raise();
        Assert.False(player.IsAudioOnly);
    }

    // Seen in the real app: a music file that had ended showed the last picture of the video played before it.
    [Fact]
    public void Music_stays_marked_as_music_after_it_ends()
    {
        var (player, engine) = Make();
        engine.HasVideo = false;
        player.Play(@"D:\song.flac", "Song", [(@"D:\song.flac", "Song")]);
        Assert.True(player.IsAudioOnly);
        engine.State = PlaybackState.Ended;
        engine.Raise();
        Assert.True(player.IsAudioOnly);
    }

    [Fact]
    public void A_video_that_ends_is_not_marked_as_music()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        engine.State = PlaybackState.Ended;
        engine.HasVideo = false;   // LibVLC lists no video track once the file has ended.
        engine.Raise();
        Assert.False(player.IsAudioOnly);
    }

    [Fact]
    public void Dispose_stops_the_engine()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        player.Dispose();
        Assert.Equal(["stop", "dispose"], engine.Calls.Skip(1));
    }

    [Fact]
    public async Task Engine_events_reach_the_ui_through_onUi()
    {
        var engine = new FakeEngine();
        var queued = new List<Action>();
        var player = new PlayerViewModel(engine, queued.Add);
        player.Play(@"D:\a.mkv", "A", List);
        queued.ForEach(run => run());
        queued.Clear();
        engine.Position = TimeSpan.FromSeconds(42);
        await Task.Run(engine.Raise);
        Assert.Equal("00:00:00", player.TimeText);
        queued.ForEach(run => run());
        Assert.Equal("00:00:42", player.TimeText);
    }

    [Fact]
    public void Play_pause_stop_and_volume_reach_the_engine()
    {
        var (player, engine) = Make();
        player.Play(@"D:\a.mkv", "A", List);
        player.PlayPauseCommand.Execute(null);
        player.StopCommand.Execute(null);
        player.Volume = 40;
        Assert.Equal(["pause", "stop"], engine.Calls.Skip(1));
        Assert.Equal(40, engine.Volume);
        Assert.False(player.HasMedia);
    }

    /// <summary>An order whose answers wait until a test gives them: the Library's, read from the database.</summary>
    sealed class SlowOrder : IPlayOrder
    {
        public readonly List<(string From, TaskCompletionSource<PlayItem?> Answer)> Asked = [];

        public Task<PlayItem?> AfterAsync(string path)
        {
            var answer = new TaskCompletionSource<PlayItem?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Asked) Asked.Add((path, answer));
            return answer.Task;
        }

        public Task<PlayItem?> BeforeAsync(string path) => Task.FromResult<PlayItem?>(null);
    }

    // Plan 2026-11-05, step 3: NEXT reads the Library's order off the UI thread. Pressed twice before the first answer
    // comes, it still steps twice: the second step waits for the first and goes on from where it landed.
    [Fact]
    public async Task Two_quick_presses_of_next_step_twice()
    {
        var (player, engine) = Make();
        var order = new SlowOrder();
        player.Play(@"D:\a.mkv", "A", order);
        var first = player.NextCommand.ExecuteAsync(null);
        var second = player.NextCommand.ExecuteAsync(null);
        order.Asked.Single().Answer.SetResult(new PlayItem(@"D:\b.mkv", "B"));
        await first;
        await WaitFor(() => order.Asked.Count == 2);
        Assert.Equal(@"D:\b.mkv", order.Asked[1].From);
        order.Asked[1].Answer.SetResult(new PlayItem(@"D:\c.mkv", "C"));
        await second;
        Assert.Equal([@"open D:\a.mkv", @"open D:\b.mkv", @"open D:\c.mkv"], engine.Calls);
        Assert.Equal("C", player.Title);
    }

    static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public void Full_screen_toggles()
    {
        var (player, _) = Make();
        player.FullScreenCommand.Execute(null);
        Assert.True(player.IsFullScreen);
        player.FullScreenCommand.Execute(null);
        Assert.False(player.IsFullScreen);
    }
}
