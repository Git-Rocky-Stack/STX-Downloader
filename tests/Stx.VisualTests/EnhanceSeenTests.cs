using Xunit;

namespace Stx.VisualTests;

/// <summary>
/// ENHANCE (Phase 6) on a real screen: what VLC's own log confirms is running, not what STX asked for. The review of
/// 2026-11-07 left UPSCALE "claimed by the rule, not yet seen running" on an Arc 140V; these see it. UPSCALE needs a
/// GPU with super resolution (Intel Arc or Xe, NVIDIA RTX with a 530 or later driver), so on one without it, such as
/// the reference PC's UHD 620, that test is skipped and says why.
/// </summary>
public partial class PlayerPlaybackTests
{
    static string SeenWith(string level, out bool gpuSuper)
    {
        using var gallery = new Gallery("player", ("STX_PLAYER_ENHANCE", level));
        string seen = "none";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && (seen = gallery.Last("enhance")) == "none") Thread.Sleep(250);
        gpuSuper = bool.Parse(gallery.Last("gpusuper"));
        return seen;
    }

    [Fact]
    public void Sharpen_is_seen_running()
    {
        Assert.Equal("Sharpen", SeenWith("sharpen", out _));
    }

    [Fact]
    public void Upscale_is_seen_running_on_a_gpu_with_super_resolution()
    {
        string seen = SeenWith("upscale", out bool gpuSuper);
        if (!gpuSuper) Assert.Skip("This GPU has no super resolution, so STX never asks for UPSCALE on it.");
        Assert.Equal("Upscale", seen);
    }
}
