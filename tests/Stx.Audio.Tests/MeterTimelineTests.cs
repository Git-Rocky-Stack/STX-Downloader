using Stx.Audio;
using Xunit;

namespace Stx.Audio.Tests;

public class MeterTimelineTests
{
    static AudioFacts Facts(double left = -6, double right = -12, IReadOnlyList<EnvelopePoint>? envelope = null) => new(
        "aac", 192, 48000, 2, 16000, -14, -1, left, right, [], envelope ?? Ramp(), MeasuredFromFile: true);

    /// <summary>Quiet at the start and loud at the end, so a sweep can be told from anything flat.</summary>
    static IReadOnlyList<EnvelopePoint> Ramp() =>
        Enumerable.Range(0, SpectrumAnalyzer.EnvelopePoints).Select(i => new EnvelopePoint(-60.0 + i * 0.3, -60.0 + i * 0.15)).ToList();

    [Fact]
    public void Sweep_is_derived_only_from_envelope()
    {
        var facts = Facts();
        Assert.Equal(MeterTimeline.Sweep(facts), MeterTimeline.Sweep(facts));
    }

    [Fact]
    public void Different_audio_gives_a_different_sweep() =>
        Assert.NotEqual(MeterTimeline.Sweep(Facts()), MeterTimeline.Sweep(Facts(envelope: Ramp().Reverse().ToList())));

    [Fact]
    public void Sweep_lasts_4_seconds_at_20fps()
    {
        Assert.Equal(80, MeterTimeline.Sweep(Facts()).Count);
        Assert.Equal(20, MeterTimeline.FramesPerSecond);
    }

    [Fact]
    public void Sweep_follows_the_measured_levels_in_order()
    {
        var sweep = MeterTimeline.Sweep(Facts());
        Assert.True(sweep[0].Left < sweep[40].Left && sweep[40].Left < sweep[^1].Left);
    }

    [Fact]
    public void Sweep_reaches_exactly_the_envelope_at_its_ends()
    {
        var sweep = MeterTimeline.Sweep(Facts());
        Assert.Equal(MeterTimeline.ToPercent(Ramp()[0].LeftDb), sweep[0].Left);
        Assert.Equal(MeterTimeline.ToPercent(Ramp()[^1].LeftDb), sweep[^1].Left);
    }

    [Fact]
    public void Hold_equals_measured_peaks()
    {
        var hold = MeterTimeline.Hold(Facts(left: -6, right: -12));
        Assert.Equal(MeterTimeline.ToPercent(-6), hold.Left);
        Assert.Equal(MeterTimeline.ToPercent(-12), hold.Right);
        Assert.Equal(hold.Left, hold.PeakLeft);
        Assert.Equal(hold.Right, hold.PeakRight);
    }

    [Fact]
    public void Sweep_carries_the_true_peak_for_the_peak_hold_segment()
    {
        var sweep = MeterTimeline.Sweep(Facts(left: -6, right: -12));
        Assert.All(sweep, f => Assert.Equal(MeterTimeline.ToPercent(-6), f.PeakLeft));
    }

    [Fact]
    public void Nothing_selected_is_dark()
    {
        Assert.Equal(0, MeterTimeline.Dark.Left);
        Assert.Equal(0, MeterTimeline.Dark.Right);
        Assert.True(double.IsNaN(MeterTimeline.Dark.PeakLeft));
        Assert.True(double.IsNaN(MeterTimeline.Dark.PeakRight));
    }

    [Fact]
    public void Reduce_motion_skips_sweep() =>
        Assert.Empty(MeterTimeline.Sweep(Facts(), reduceMotion: true));

    [Fact]
    public void Reported_facts_have_nothing_to_show_so_meters_stay_dark()
    {
        var reported = ReportedFacts.From(new YtDlpFormat("opus", 128, 48000, 2));
        Assert.Empty(MeterTimeline.Sweep(reported));
        Assert.Equal(MeterTimeline.Dark, MeterTimeline.Hold(reported));
    }

    // Levels the site merely claims must never move a meter, even if they were somehow filled in.
    [Fact]
    public void Facts_not_measured_from_the_file_never_drive_the_meters()
    {
        var claimed = Facts() with { MeasuredFromFile = false };
        Assert.Empty(MeterTimeline.Sweep(claimed));
        Assert.Equal(MeterTimeline.Dark, MeterTimeline.Hold(claimed));
    }

    [Fact]
    public void Silence_measures_as_empty_meters()
    {
        var silent = Facts(double.NegativeInfinity, double.NegativeInfinity, Enumerable.Repeat(new EnvelopePoint(double.NegativeInfinity, double.NegativeInfinity), 200).ToList());
        Assert.All(MeterTimeline.Sweep(silent), f => Assert.Equal(0, f.Left));
        var hold = MeterTimeline.Hold(silent);
        Assert.Equal(0, hold.Left);
        Assert.True(double.IsNaN(hold.PeakLeft), "no peak segment for silence");
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-60, 0)]
    [InlineData(-30, 50)]
    [InlineData(-90, 0)]
    [InlineData(3, 100)]
    public void Decibels_map_onto_the_meter_scale(double db, double percent) =>
        Assert.Equal(percent, MeterTimeline.ToPercent(db));

    [Fact]
    public void An_envelope_shorter_than_the_sweep_still_gives_every_frame()
    {
        var short5 = Enumerable.Range(0, 5).Select(i => new EnvelopePoint(-50.0 + i, -50.0 + i)).ToList();
        Assert.Equal(80, MeterTimeline.Sweep(Facts(envelope: short5)).Count);
    }

    [Fact]
    public void There_is_no_randomness_anywhere_in_the_meter_code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        string source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Stx.Audio", "MeterTimeline.cs"));
        Assert.DoesNotContain("Random", source);
        Assert.DoesNotContain("Guid", source);
        Assert.DoesNotContain("DateTime", source);
    }
}
