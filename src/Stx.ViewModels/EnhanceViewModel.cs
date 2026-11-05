using CommunityToolkit.Mvvm.ComponentModel;
using Stx.Core.Playback;

namespace Stx.ViewModels;

/// <summary>
/// The ENHANCE knob (Phase 6). The knob keeps what was asked for; the engine gets what can run, held under
/// PERFORMANCE's ceiling and to what the GPU has; and the readout says what VLC confirmed is on the picture, or why not.
/// </summary>
public sealed class EnhanceViewModel : ObservableObject
{
    readonly SettingsViewModel _settings;
    readonly Func<PerformanceProfile> _profile;
    readonly GpuFacts? _gpu;
    readonly Action<EnhanceLevel> _apply;
    readonly Func<EnhanceLevel?> _seen;
    string _status = "";

    /// <param name="apply">Sets the engine's level; only called when it changes.</param>
    /// <param name="seen">What the engine's log confirmed is running.</param>
    public EnhanceViewModel(SettingsViewModel settings, Func<PerformanceProfile> profile, GpuFacts? gpu, Action<EnhanceLevel> apply, Func<EnhanceLevel?> seen)
    {
        _settings = settings;
        _profile = profile;
        _gpu = gpu;
        _apply = apply;
        _seen = seen;
        Applied = Enhance.Effective(settings.Enhance, profile(), Enhance.SuperResolution(gpu));
        apply(Applied);
        Refresh();
    }

    public static IReadOnlyList<string> Labels => Enhance.Labels;

    /// <summary>The knob's position: what was asked for.</summary>
    public EnhanceLevel Wanted
    {
        get => _settings.Enhance;
        set
        {
            if (value == _settings.Enhance) return;
            _settings.Enhance = value;
            OnPropertyChanged();
            Reapply();
        }
    }

    /// <summary>What the engine runs.</summary>
    public EnhanceLevel Applied { get; private set; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>After the knob or PERFORMANCE changes: gives the engine the level that can run, if it's different.</summary>
    public void Reapply()
    {
        var level = Enhance.Effective(_settings.Enhance, _profile(), Enhance.SuperResolution(_gpu));
        if (level != Applied)
        {
            Applied = level;
            _apply(level);
        }
        Refresh();
    }

    /// <summary>Reads again what the engine confirmed.</summary>
    public void Refresh() => Status = Enhance.Status(_settings.Enhance, _profile(), _gpu, _seen());
}
