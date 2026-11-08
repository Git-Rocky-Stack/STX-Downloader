using Stx.Audio;
using Stx.Core.Audio;
using Stx.Core.Jobs;
using Stx.Core.Queue;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

public class SourceTruthTests
{
    static AudioFacts Measured(string codec = "aac", double? rolloff = 16000) => new(
        codec, 192, 48000, 2, rolloff, -14.2, -1.5, -6, -12,
        [new SpectrumBand(1000, -30)],
        Enumerable.Range(0, SpectrumAnalyzer.EnvelopePoints).Select(i => new EnvelopePoint(-60.0 + i * 0.2, -60.0 + i * 0.1)).ToList(),
        MeasuredFromFile: true);

    static readonly AudioFacts Reported = ReportedFacts.From(new YtDlpFormat("opus", 129, 48000, 2));

    static SourceTruthViewModel Panel(bool reduceMotion = false) => new(() => reduceMotion);

    [Fact]
    public void No_selection_is_dark()
    {
        var panel = Panel();
        Assert.Equal(MeterTimeline.Dark, panel.Meters);
        Assert.Empty(panel.Spectrum);
        Assert.False(panel.HasContent);
        Assert.Equal("", panel.SourceLine);
    }

    [Fact]
    public void Clearing_a_selection_goes_dark_again()
    {
        var panel = Panel();
        panel.Show("Song", Measured());
        panel.Clear();
        Assert.Equal(MeterTimeline.Dark, panel.Meters);
        Assert.False(panel.HasContent);
    }

    [Fact]
    public void Post_download_values_labeled_measured()
    {
        var panel = Panel();
        panel.Show("Song", Measured());
        Assert.Equal("Measured", panel.FactsLabel);
        Assert.Contains("AAC", panel.SourceLine);
        Assert.Contains("192", panel.SourceLine);
        Assert.Contains("48", panel.SourceLine);
        Assert.Contains("16", panel.RollOffLine);
        Assert.Contains("-14.2", panel.LoudnessLine);
        Assert.Contains("-1.5", panel.LoudnessLine);
        Assert.NotEmpty(panel.Spectrum);
    }

    [Fact]
    public void Pre_download_values_labeled_reported()
    {
        var panel = Panel();
        panel.Show("Song", Reported);
        Assert.Equal("Reported", panel.FactsLabel);
        Assert.Contains("OPUS", panel.SourceLine);
        Assert.Contains("REPORTED", panel.SourceLine);
        Assert.Equal(MeterTimeline.Dark, panel.Meters);
        Assert.Empty(panel.Spectrum);
        Assert.Equal("", panel.LoudnessLine);
    }

    [Fact]
    public void No_audio_stream_says_so()
    {
        var panel = Panel();
        panel.ShowNoAudio("Silent film");
        Assert.True(panel.HasContent);
        Assert.Equal(AudioFacts.NoAudioMessage.ToUpperInvariant(), panel.SourceLine);
        Assert.Equal(MeterTimeline.Dark, panel.Meters);
        Assert.Equal("", panel.VerdictLine);
    }

    [Fact]
    public void A_file_with_no_cutoff_says_full_band()
    {
        var panel = Panel();
        panel.Show("Song", Measured("flac", 23988));
        Assert.Contains("FULL BAND", panel.RollOffLine);
    }

    [Fact]
    public void Missing_loudness_is_left_blank_not_invented()
    {
        var panel = Panel();
        panel.Show("Song", Measured() with { LoudnessLufs = null, TruePeakDb = null });
        Assert.Equal("", panel.LoudnessLine);
    }

    [Fact]
    public void Verdict_updates_with_rotary()
    {
        var panel = Panel();
        panel.Show("Song", Measured());
        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(OutputChoice.Original);
        Assert.Equal("BIT-PERFECT", panel.VerdictLine);

        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(OutputChoice.Mp3);
        Assert.Equal("RE-ENCODED; SLIGHT QUALITY LOSS", panel.VerdictLine);

        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(OutputChoice.Wav);
        Assert.Equal("LARGER FILE, SAME SOUND", panel.VerdictLine);
    }

    [Fact]
    public void Lossy_source_to_flac_gets_the_honest_wording()
    {
        var panel = Panel();
        panel.Show("Song", Measured("aac"));
        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(OutputChoice.Flac);
        Assert.Equal("LOSSLESS COPY OF A LOSSY SOURCE; NO QUALITY GAINED", panel.VerdictLine);
    }

    [Fact]
    public void Lossless_source_to_flac_is_not_called_a_lossy_copy()
    {
        var panel = Panel();
        panel.Show("Song", Measured("alac"));
        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(OutputChoice.Flac);
        Assert.Equal("LOSSLESS; NOTHING LOST", panel.VerdictLine);
    }

    [Theory]
    [InlineData(OutputChoice.Flac)]
    [InlineData(OutputChoice.Wav)]
    [InlineData(OutputChoice.Aiff)]
    public void Pointless_conversion_warns_but_is_allowed(OutputChoice output)
    {
        var panel = Panel();
        panel.Show("Song", Measured("aac"));
        panel.SelectedOutputIndex = SourceTruthViewModel.Outputs.ToList().IndexOf(output);
        Assert.False(string.IsNullOrEmpty(panel.Warning));
        Assert.True(panel.CanConvert);
    }

    [Fact]
    public void Keeping_the_original_needs_no_warning()
    {
        var panel = Panel();
        panel.Show("Song", Measured("aac"));
        panel.SelectedOutputIndex = 0;
        Assert.Equal(OutputChoice.Original, panel.SelectedOutput);
        Assert.Equal("", panel.Warning);
        Assert.False(panel.CanConvert, "the original is what you already have");
    }

    [Fact]
    public void The_default_is_always_to_keep_the_original() =>
        Assert.Equal(OutputChoice.Original, Panel().SelectedOutput);

    [Fact]
    public void The_rotary_has_a_label_for_every_position() =>
        Assert.Equal(SourceTruthViewModel.Outputs.Count, SourceTruthViewModel.OutputLabels.Count);

    [Fact]
    public void Finished_analysis_triggers_sweep_then_hold()
    {
        var panel = Panel();
        var facts = Measured();
        panel.ShowFinishedAnalysis("Song", facts);
        Assert.True(panel.IsSweeping);

        var sweep = MeterTimeline.Sweep(facts);
        Assert.Equal(sweep[0], panel.Meters);
        for (int i = 1; i < sweep.Count; i++)
        {
            panel.AdvanceSweep();
            Assert.Equal(sweep[i], panel.Meters);
        }

        panel.AdvanceSweep();
        Assert.False(panel.IsSweeping);
        Assert.Equal(MeterTimeline.Hold(facts), panel.Meters);
    }

    [Fact]
    public void Selecting_an_item_holds_without_sweeping()
    {
        var panel = Panel();
        panel.Show("Song", Measured());
        Assert.False(panel.IsSweeping);
        Assert.Equal(MeterTimeline.Hold(Measured()), panel.Meters);
    }

    [Fact]
    public void Reduce_motion_goes_straight_to_hold()
    {
        var panel = Panel(reduceMotion: true);
        panel.ShowFinishedAnalysis("Song", Measured());
        Assert.False(panel.IsSweeping);
        Assert.Equal(MeterTimeline.Hold(Measured()), panel.Meters);
    }

    [Fact]
    public void Selecting_something_else_during_a_sweep_stops_it()
    {
        var panel = Panel();
        panel.ShowFinishedAnalysis("Song", Measured());
        panel.Clear();
        Assert.False(panel.IsSweeping);
        panel.AdvanceSweep();
        Assert.Equal(MeterTimeline.Dark, panel.Meters);
    }
}

sealed class LibraryCatalog : InMemoryLibrary
{
    /// <summary>Newest first, as the database lists them.</summary>
    public readonly List<LibraryItem> Stored = new();
    protected override IReadOnlyList<LibraryItem> AllItems() => Stored;
}

public class LibraryTests
{
    readonly LibraryCatalog _catalog = new();
    readonly MemoryCatalog _media = new();
    readonly MemoryJobs _jobs = new();
    readonly HandRunner _runner = new();
    readonly ShellSpy _shell = new();
    readonly HashSet<string> _files = new();
    readonly List<LibraryRowViewModel> _played = new();

    LibraryViewModel Library()
    {
        var engine = new QueueEngine(_jobs, _runner, new QueuePolicy(), new SystemClock(), new CaptureVault());
        return new LibraryViewModel(_catalog, engine, _shell, _files.Contains, _played.Add);
    }

    void Add(string title, string path, string format = SourceViewModel.OriginalFormat, bool onDisk = true)
    {
        _catalog.Stored.Add(new LibraryItem(_catalog.Stored.Count + 1, title, "https://x", [new LibraryFile(_catalog.Stored.Count + 1, path, format)]));
        if (onDisk) _files.Add(path);
    }

    [Fact]
    public void Empty_library_shows_empty_state()
    {
        var library = Library();
        library.Refresh();
        Assert.True(library.IsEmpty);
        Assert.Empty(library.Rows);
    }

    [Fact]
    public void Saved_items_are_listed()
    {
        Add("Song", @"D:\m\song.mp4");
        var library = Library();
        library.Refresh();
        Assert.False(library.IsEmpty);
        var row = Assert.Single(library.Rows);
        Assert.Equal("Song", row.Title);
        Assert.False(row.IsMissing);
    }

    [Fact]
    public void One_file_reached_by_two_links_is_listed_once_under_whichever_saved_it_first()
    {
        // The catalog lists items newest first, so the first to save it comes last here.
        _catalog.Stored.Add(new LibraryItem(2, "Copy", "https://x/2", [new LibraryFile(2, @"D:\m\SONG.mp4", SourceViewModel.OriginalFormat)]));
        _catalog.Stored.Add(new LibraryItem(1, "Song", "https://x/1", [new LibraryFile(1, @"D:\m\song.mp4", SourceViewModel.OriginalFormat)]));
        _files.Add(@"D:\m\SONG.mp4");
        _files.Add(@"D:\m\song.mp4");
        var library = Library();

        library.Refresh();

        Assert.Equal("Song", Assert.Single(library.Rows).Title);
    }

    [Fact]
    public void One_item_can_have_several_files()
    {
        _catalog.Stored.Add(new LibraryItem(1, "Song", "https://x", [new LibraryFile(1, @"D:\s.mp4", "Original"), new LibraryFile(2, @"D:\s.flac", "Flac")]));
        _files.Add(@"D:\s.mp4");
        _files.Add(@"D:\s.flac");
        var library = Library();
        library.Refresh();
        Assert.Equal(2, library.Rows.Count);
    }

    [Theory]
    [InlineData(@"D:\s.mp4", "Original", "MP4 · ORIGINAL")]
    [InlineData(@"D:\s.flac", "Flac", "FLAC · CONVERTED")]
    [InlineData(@"D:\s (2).mp4", "Car USB", "MP4 · CAR USB")]
    public void Status_says_what_the_file_is_without_repeating_itself(string path, string format, string status)
    {
        _catalog.Stored.Add(new LibraryItem(1, "Song", "https://x", [new LibraryFile(1, path, format)]));
        _files.Add(path);
        var library = Library();
        library.Refresh();
        Assert.Equal(status, library.Rows.Single().Status);
    }

    [Fact]
    public void Missing_file_shows_missing_state()
    {
        Add("Song", @"D:\moved\song.mp4", onDisk: false);
        var library = Library();
        library.Refresh();
        var row = library.Rows.Single();
        Assert.True(row.IsMissing);
        Assert.Contains("MISSING", row.Status);
        Assert.False(row.CanOpen);
        Assert.False(row.CanConvert);
    }

    // OPEN plays the file on the PLAYER plate; it never hands the file to another app.
    [Fact]
    public void Open_plays_inside_STX_and_never_hands_off()
    {
        Add("Song", @"D:\m\song.mp4");
        var library = Library();
        library.Refresh();
        library.OpenCommand.Execute(library.Rows[0]);
        Assert.Equal([library.Rows[0]], _played);
        Assert.Empty(_shell.FileActions);
    }

    [Fact]
    public void Find_file_still_shows_it_in_its_folder()
    {
        Add("Song", @"D:\m\song.mp4");
        var library = Library();
        library.Refresh();
        library.RevealCommand.Execute(library.Rows[0]);
        Assert.Equal([@"reveal:D:\m\song.mp4"], _shell.FileActions);
    }

    [Fact]
    public void Open_is_disabled_for_a_missing_file()
    {
        Add("Song", @"D:\moved\song.mp4", onDisk: false);
        var library = Library();
        library.Refresh();
        library.OpenCommand.Execute(library.Rows[0]);
        Assert.Empty(_played);
        Assert.Empty(_shell.FileActions);
    }

    [Fact]
    public void Convert_from_library_creates_job_and_keeps_original()
    {
        Add("Song", @"D:\m\song.mp4");
        var library = Library();
        library.Refresh();

        library.ConvertCommand.Execute(new ConvertAsk(library.Rows[0], OutputChoice.Flac));

        var job = Assert.Single(_jobs.All());
        Assert.Equal(JobKind.Convert, job.Kind);
        var request = ConversionPlan.Parse(job.Preset)!;
        Assert.Equal(OutputChoice.Flac, request.Output);
        Assert.Equal(1, request.FileId);
        Assert.Null(request.PresetName);
        Assert.Contains(@"D:\m\song.mp4", _files);
    }

    [Fact]
    public void A_device_preset_conversion_names_its_preset()
    {
        Add("Song", @"D:\m\song.mp4");
        var library = Library();
        library.Refresh();
        library.ConvertPresetCommand.Execute(new PresetAsk(library.Rows[0], "Car USB"));
        var request = ConversionPlan.Parse(Assert.Single(_jobs.All()).Preset)!;
        Assert.Equal("Car USB", request.PresetName);
    }

    [Fact]
    public void A_missing_file_cannot_be_converted()
    {
        Add("Song", @"D:\moved\song.mp4", onDisk: false);
        var library = Library();
        library.Refresh();
        library.ConvertCommand.Execute(new ConvertAsk(library.Rows[0], OutputChoice.Flac));
        Assert.Empty(_jobs.All());
    }

    [Fact]
    public void Converting_to_a_format_already_saved_does_not_make_a_duplicate()
    {
        _catalog.Stored.Add(new LibraryItem(1, "Song", "https://x", [new LibraryFile(1, @"D:\s.mp4", "Original"), new LibraryFile(2, @"D:\s.flac", "Flac")]));
        _files.Add(@"D:\s.mp4");
        _files.Add(@"D:\s.flac");
        var library = Library();
        library.Refresh();
        library.ConvertCommand.Execute(new ConvertAsk(library.Rows[0], OutputChoice.Flac));
        Assert.Empty(_jobs.All());
        Assert.Contains("already", library.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("convert:Flac:7", OutputChoice.Flac, 7L, null)]
    [InlineData("convert:Mp4:7:Car USB", OutputChoice.Mp4, 7L, "Car USB")]
    public void Conversion_plans_round_trip(string text, OutputChoice output, long file, string? preset)
    {
        var plan = ConversionPlan.Parse(text)!;
        Assert.Equal((output, file, preset), (plan.Output, plan.FileId, plan.PresetName));
        Assert.Equal(text, plan.ToString());
    }

    [Theory]
    [InlineData("Keep original")]
    [InlineData("convert:Nope:7")]
    [InlineData("convert:Flac:x")]
    [InlineData("convert:Flac")]
    [InlineData("")]
    public void Anything_else_is_not_a_conversion_plan(string text) => Assert.Null(ConversionPlan.Parse(text));
}

public class ChooseBeforeDownloadTests : ConsoleFixture
{
    [Fact]
    public async Task A_format_chosen_before_download_is_carried_on_the_job()
    {
        var source = new SourceViewModel(new ScriptedAnalyzer(_ => One()), Catalog, Engine, () => OutputChoice.Flac) { Link = "https://x/abc" };
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal(OutputChoice.Flac, ConversionPlan.AfterDownload(Assert.Single(Jobs.All()).Preset));
    }

    [Fact]
    public async Task Keeping_the_original_needs_no_second_step()
    {
        var source = new SourceViewModel(new ScriptedAnalyzer(_ => One()), Catalog, Engine, () => OutputChoice.Original) { Link = "https://x/abc" };
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.Null(ConversionPlan.AfterDownload(Assert.Single(Jobs.All()).Preset));
    }

    [Fact]
    public async Task What_the_site_reports_is_offered_before_downloading()
    {
        var reported = new YtDlpFormat("opus", 129, 48000, 2);
        var source = Source(new ScriptedAnalyzer(_ => One() with { BestAudio = reported }));
        source.Link = "https://x/abc";
        await source.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(source.ReportedAudio!.MeasuredFromFile);
        Assert.Equal("opus", source.ReportedAudio.Codec);
    }
}
