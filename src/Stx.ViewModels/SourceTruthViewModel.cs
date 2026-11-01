using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Stx.Audio;
using Stx.Core.Audio;

namespace Stx.ViewModels;

/// <summary>
/// The Source Truth panel: what's really in a file, and what each output would honestly do to it. Meters are dark
/// with nothing selected, hold the measured peaks at rest, and sweep the measured levels after an analysis finishes.
/// </summary>
public sealed class SourceTruthViewModel(Func<bool> reduceMotion) : ObservableObject
{
    const double FullBandShare = 0.97;

    public static readonly IReadOnlyList<OutputChoice> Outputs =
    [
        OutputChoice.Original, OutputChoice.Flac, OutputChoice.Alac, OutputChoice.Wav, OutputChoice.Aiff,
        OutputChoice.AacM4a, OutputChoice.Mp3, OutputChoice.Opus, OutputChoice.OggVorbis,
    ];

    public static readonly IReadOnlyList<string> OutputLabels = ["ORIGINAL", "FLAC", "ALAC", "WAV", "AIFF", "AAC", "MP3", "OPUS", "OGG"];

    AudioFacts? _facts;
    bool _noAudio;
    string _title = "";
    int _outputIndex;
    MeterFrame _meters = MeterTimeline.Dark;
    IReadOnlyList<MeterFrame> _sweep = [];
    int _sweepAt;

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public MeterFrame Meters { get => _meters; private set => SetProperty(ref _meters, value); }
    public bool IsSweeping => _sweepAt < _sweep.Count;
    public bool HasContent => _facts is not null || _noAudio;
    public IReadOnlyList<SpectrumBand> Spectrum => _facts?.Spectrum ?? [];
    public double? RollOffHz => _facts?.RollOffHz;
    public string FactsLabel => _facts?.Label ?? "";
    public OutputChoice SelectedOutput => Outputs[_outputIndex];

    public int SelectedOutputIndex
    {
        get => _outputIndex;
        set
        {
            if (!SetProperty(ref _outputIndex, Math.Clamp(value, 0, Outputs.Count - 1))) return;
            OnPropertyChanged(nameof(SelectedOutput));
            OnPropertyChanged(nameof(VerdictLine));
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(CanConvert));
        }
    }

    public string SourceLine
    {
        get
        {
            if (_noAudio) return AudioFacts.NoAudioMessage.ToUpperInvariant();
            if (_facts is not { } f) return "";
            var parts = new List<string> { f.Codec.ToUpperInvariant() };
            if (f.BitrateKbps is { } kbps) parts.Add($"{kbps} KBPS");
            if (f.SampleRate is { } rate) parts.Add((rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " KHZ");
            if (f.Channels is { } channels) parts.Add($"{channels} CH");
            string line = string.Join(" · ", parts);
            return f.MeasuredFromFile ? line : "REPORTED: " + line;
        }
    }

    public string RollOffLine
    {
        get
        {
            if (_facts?.RollOffHz is not { } hz) return "";
            if (_facts.SampleRate is { } rate && hz >= rate / 2.0 * FullBandShare) return "FULL BAND · NO CUTOFF";
            return "ROLL-OFF " + (hz / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " KHZ";
        }
    }

    public string LoudnessLine
    {
        get
        {
            if (_facts is not { } f) return "";
            var parts = new List<string>();
            if (f.LoudnessLufs is { } lufs) parts.Add(lufs.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS");
            if (f.TruePeakDb is { } peak) parts.Add("PEAK " + peak.ToString("0.0", CultureInfo.InvariantCulture) + " DB");
            return string.Join(" · ", parts);
        }
    }

    public string VerdictLine => _facts is { } f ? Verdict.For(SelectedOutput, f.IsLossy).ToUpperInvariant() : "";

    /// <summary>Shown for a conversion that adds nothing. It's your call, so it's never blocked.</summary>
    public string Warning => _facts is { IsLossy: true } && SelectedOutput is OutputChoice.Flac or OutputChoice.Alac or OutputChoice.Wav or OutputChoice.Aiff
        ? "This adds no quality, because the source is already lossy."
        : "";

    public bool CanConvert => _facts is not null && SelectedOutput != OutputChoice.Original;

    public void Show(string title, AudioFacts facts) => Load(title, facts, noAudio: false, MeterTimeline.Hold(facts), []);

    public void ShowFinishedAnalysis(string title, AudioFacts facts)
    {
        var sweep = MeterTimeline.Sweep(facts, reduceMotion());
        Load(title, facts, noAudio: false, sweep.Count > 0 ? sweep[0] : MeterTimeline.Hold(facts), sweep);
    }

    public void ShowNoAudio(string title) => Load(title, null, noAudio: true, MeterTimeline.Dark, []);

    public void Clear() => Load("", null, noAudio: false, MeterTimeline.Dark, []);

    /// <summary>Moves on one frame. The panel calls this only while IsSweeping, so at rest nothing runs.</summary>
    public void AdvanceSweep()
    {
        if (!IsSweeping) return;
        _sweepAt++;
        Meters = IsSweeping ? _sweep[_sweepAt] : MeterTimeline.Hold(_facts!);
        OnPropertyChanged(nameof(IsSweeping));
    }

    void Load(string title, AudioFacts? facts, bool noAudio, MeterFrame meters, IReadOnlyList<MeterFrame> sweep)
    {
        _facts = facts;
        _noAudio = noAudio;
        _sweep = sweep;
        _sweepAt = 0;
        Title = title;
        Meters = meters;
        foreach (string name in new[] { nameof(HasContent), nameof(Spectrum), nameof(RollOffHz), nameof(FactsLabel), nameof(SourceLine), nameof(RollOffLine), nameof(LoudnessLine), nameof(VerdictLine), nameof(Warning), nameof(CanConvert), nameof(IsSweeping) })
            OnPropertyChanged(name);
    }
}
