using System.Diagnostics;
using Xunit;

namespace Stx.VisualTests;

/// <summary>
/// HDR10 video on an HDR screen (2026-11-07, a laptop panel with Windows' HDR on): VLC's Direct3D 11 output, drawing
/// into STX's WinUI swap chain, chooses the screen's HDR colour space (Rec.2020 with the PQ curve, SMPTE ST 2084) for
/// an HDR10 file, so it isn't flattened to SDR. Where the screen isn't in HDR mode VLC doesn't offer that colour space,
/// and the test is skipped and says so.
/// </summary>
public partial class PlayerPlaybackTests
{
    [Fact]
    public void Hdr10_video_reaches_an_hdr_screen_as_hdr()
    {
        string clip = Output("hdr10-1080p30.mkv");
        if (!File.Exists(clip))
        {
            var make = Process.Start(new ProcessStartInfo("ffmpeg",
                "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1920x1080:rate=30 -t 8 -c:v libx265 -pix_fmt yuv420p10le " +
                "-x265-params \"log-level=error:hdr10=1:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:" +
                "master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400\" " + $"\"{clip}\"")
            { UseShellExecute = false })!;
            make.WaitForExit();
            Assert.True(make.ExitCode == 0 && File.Exists(clip), "FFmpeg with libx265 is needed to make the HDR10 test file.");
        }
        string vlcLog = Output($"hdr10-vlc-{Guid.NewGuid():N}.log");
        using (var app = new Gallery("player", ("STX_PLAYER_FILE", clip), ("STX_PLAYER_VLCLOG", vlcLog)))
            Assert.True(Within(15, () => app.Last("state") == "Playing" && double.Parse(app.Last("time"), System.Globalization.CultureInfo.InvariantCulture) > 1000), "It never played.");
        string[] lines = File.ReadAllLines(vlcLog);
        if (!lines.Any(l => l.StartsWith("direct3d11: supports colorspace RGB Rec.2020 gamma:2084", StringComparison.Ordinal)))
            Assert.Skip("This screen isn't in HDR mode (VLC's output offers no PQ colour space), so there's no HDR to reach.");
        Assert.Contains(lines, l => l.StartsWith("direct3d11: using colorspace RGB Rec.2020 gamma:2084", StringComparison.Ordinal));
    }
}
