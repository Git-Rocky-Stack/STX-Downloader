using Stx.Core.Playback;
using Stx.Core.Settings;
using Stx.ViewModels;
using Xunit;

namespace Stx.App.Tests;

public sealed class EnhanceViewModelTests : IDisposable
{
    readonly string _file = Path.Combine(Path.GetTempPath(), "stx-enhance-" + Guid.NewGuid() + ".json");
    readonly List<EnhanceLevel> _applied = [];
    PerformanceProfile _profile = new(true, 4, EnhanceLevel.Sharpen);
    EnhanceLevel? _seen;

    public void Dispose() => File.Delete(_file);

    EnhanceViewModel Make(GpuFacts? gpu = null) =>
        new(new SettingsViewModel(new SettingsStore(_file), new FakeStartup()), () => _profile, gpu, _applied.Add, () => _seen);

    [Fact]
    public void Off_by_default_and_the_engine_is_told_once()
    {
        var enhance = Make();
        Assert.Equal(EnhanceLevel.Off, enhance.Wanted);
        Assert.Equal([EnhanceLevel.Off], _applied);
        Assert.Equal("OFF", enhance.Status);
    }

    [Fact]
    public void Turning_the_knob_lasts_and_reaches_the_engine()
    {
        var enhance = Make();
        enhance.Wanted = EnhanceLevel.Sharpen;
        Assert.Equal(EnhanceLevel.Sharpen, _applied[^1]);
        Assert.Equal(EnhanceLevel.Sharpen, Make().Wanted);
    }

    [Fact]
    public void Upscale_without_the_gpu_keeps_the_knob_but_runs_nothing()
    {
        _profile = new(true, 8, EnhanceLevel.Upscale);
        var enhance = Make(new GpuFacts(GpuFacts.Intel, "Intel(R) UHD Graphics 620", null));
        enhance.Wanted = EnhanceLevel.Upscale;
        Assert.Equal(EnhanceLevel.Upscale, enhance.Wanted);
        Assert.Equal(EnhanceLevel.Off, enhance.Applied);
        Assert.Equal([EnhanceLevel.Off], _applied);
        Assert.StartsWith("UPSCALE UNAVAILABLE", enhance.Status);
    }

    [Fact]
    public void A_lower_performance_ceiling_takes_it_back_down()
    {
        var enhance = Make();
        enhance.Wanted = EnhanceLevel.Sharpen;
        _profile = new(true, 2, EnhanceLevel.Off);
        enhance.Reapply();
        Assert.Equal(EnhanceLevel.Off, _applied[^1]);
        Assert.StartsWith("OFF: PERFORMANCE ECO", enhance.Status);
    }

    [Fact]
    public void The_readout_follows_what_the_engine_confirms()
    {
        var enhance = Make();
        enhance.Wanted = EnhanceLevel.Sharpen;
        Assert.EndsWith("WAITING FOR A PICTURE", enhance.Status);
        _seen = EnhanceLevel.Sharpen;
        enhance.Refresh();
        Assert.EndsWith("ON SCREEN ONLY", enhance.Status);
    }

    [Fact]
    public void A_hand_edited_level_loads_as_off()
    {
        File.WriteAllText(_file, "{\"Enhance\": 7}");
        Assert.Equal(EnhanceLevel.Off, Make().Wanted);
    }
}
