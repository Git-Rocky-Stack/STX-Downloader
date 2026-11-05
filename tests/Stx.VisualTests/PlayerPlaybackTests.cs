using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Xunit;

namespace Stx.VisualTests;

/// <summary>
/// The PLAYER plate, driven as a person would: real playback through VLC's engine, real full screen, real clicks.
/// Every real click or key first makes STX the topmost window and checks that the point under the cursor belongs to
/// STX; if it doesn't, the test fails without sending anything, so input can never land in another window.
/// </summary>
public partial class PlayerPlaybackTests
{
    // Inside the picture, in the 944x601 capture of the player gallery.
    static readonly Rectangle Picture = new(56, 96, 330, 170);

    static PlayerPlaybackTests() => SetProcessDpiAwarenessContext(-4 /* per-monitor v2 */);

    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    static string Output(string name)
    {
        string file = Path.Combine(Root(), "tests", "Stx.VisualTests", "output", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        return file;
    }

    static string Exe()
    {
        string exe = Path.Combine(Root(), "src", "Stx.App", "bin", "x64", "Release", "net10.0-windows10.0.22621.0", "win-x64", "STXDownloader.exe");
        Assert.True(File.Exists(exe), $"Build the Release app first: {exe}");
        return exe;
    }

    /// <summary>A running player gallery, its log, and its window. Killed on dispose.</summary>
    sealed class Gallery : IDisposable
    {
        public Process App { get; }
        public string Log { get; }
        public AutomationElement Window { get; }

        public Gallery(string name, params (string Key, string Value)[] env)
        {
            Log = Output($"player-{name}-{Guid.NewGuid():N}.log");
            var start = new ProcessStartInfo(Exe(), $"--gallery={name} --mode=dark") { UseShellExecute = false };
            start.Environment["STX_PLAYER_LOG"] = Log;
            foreach (var (key, value) in env) start.Environment[key] = value;
            App = Process.Start(start)!;
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (App.MainWindowHandle == IntPtr.Zero && DateTime.UtcNow < deadline) { Thread.Sleep(100); App.Refresh(); }
            Assert.NotEqual(IntPtr.Zero, App.MainWindowHandle);
            Window = AutomationElement.FromHandle(App.MainWindowHandle);
        }

        public IntPtr Handle => App.MainWindowHandle;

        /// <summary>The newest log line's value for a key, such as "time" or "swapchains".</summary>
        public string Last(string key)
        {
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(Log))
                {
                    var line = File.ReadAllLines(Log).LastOrDefault();
                    if (line is not null && line.Split(' ').FirstOrDefault(p => p.StartsWith(key + "=", StringComparison.Ordinal)) is { } part)
                        return part[(key.Length + 1)..];
                }
                Thread.Sleep(100);
            }
            throw new InvalidOperationException($"No '{key}' in {Log}");
        }

        public double Number(string key) => double.Parse(Last(key).Split('/')[0], CultureInfo.InvariantCulture);

        public AutomationElement Pad(string name) =>
            Find(Window, e => e.Current.Name == name && e.GetCurrentPropertyValue(AutomationElement.IsInvokePatternAvailableProperty) is true)
            ?? throw new InvalidOperationException($"No pad named {name}");

        public void Invoke(string pad) => ((InvokePattern)Pad(pad).GetCurrentPattern(InvokePattern.Pattern)).Invoke();

        public void Dispose()
        {
            if (!App.HasExited) App.Kill();
            App.WaitForExit(5000);
        }
    }

    static AutomationElement? Find(AutomationElement root, Func<AutomationElement, bool> match, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        do
        {
            foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                if (match(e)) return e;
            Thread.Sleep(200);
        } while (DateTime.UtcNow < deadline);
        return null;
    }

    static bool Within(double seconds, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline) { if (condition()) return true; Thread.Sleep(100); }
        return condition();
    }

    static string Capture(string label, int settleSeconds)
    {
        string root = Root();
        string file = Output($"player-{label}.png");
        var capture = Process.Start(new ProcessStartInfo("pwsh",
            $"-NoProfile -File \"{Path.Combine(root, "tools", "Capture-Gallery.ps1")}\" -Name player -Mode dark -Live -Out \"{file}\" -SettleSeconds {settleSeconds}")
        { UseShellExecute = false, RedirectStandardOutput = true })!;
        capture.StandardOutput.ReadToEnd();
        capture.WaitForExit();
        Assert.Equal(0, capture.ExitCode);
        return file;
    }

    static double Changed(string a, string b)
    {
        using var x = new Bitmap(a);
        using var y = new Bitmap(b);
        int changed = 0, total = 0;
        for (int row = Picture.Top; row < Picture.Bottom; row += 2)
        for (int col = Picture.Left; col < Picture.Right; col += 2)
        {
            Color p = x.GetPixel(col, row), q = y.GetPixel(col, row);
            if (Math.Abs(p.R - q.R) > 12 || Math.Abs(p.G - q.G) > 12 || Math.Abs(p.B - q.B) > 12) changed++;
            total++;
        }
        return changed / (double)total;
    }

    // Puts STX on top of everything, then clicks the centre of an element with real input, but only if the point is
    // STX's own window. Clicking also makes STX the foreground window, which a real key press needs.
    static void RealClick(Gallery app, AutomationElement element)
    {
        SetWindowPos(app.Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 /* NOSIZE | NOMOVE */);
        var r = element.Current.BoundingRectangle;
        var point = new POINT { X = (int)(r.Left + r.Width / 2), Y = (int)(r.Top + r.Height / 2) };
        var under = WindowFromPoint(point);
        GetWindowThreadProcessId(under, out uint owner);
        Assert.True(owner == (uint)app.App.Id, $"The point {point.X},{point.Y} belongs to process {owner}, not STX ({app.App.Id}); nothing was clicked.");
        SetCursorPos(point.X, point.Y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    static void RealEscape(Gallery app)
    {
        Assert.True(GetForegroundWindow() == app.Handle, "STX isn't the foreground window, so Esc wasn't sent.");
        keybd_event(0x1B, 0, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0, 0x0002, UIntPtr.Zero);
    }

    static RECT WindowRect(IntPtr handle) { GetWindowRect(handle, out var r); return r; }

    static RECT MonitorRect(IntPtr handle)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(handle, 2), ref info);
        return info.Monitor;
    }

    [Fact]
    public void The_player_shows_moving_video()
    {
        string early = Capture("early", 2);
        string later = Capture("later", 5);
        double changed = Changed(early, later);
        Assert.True(changed > 0.05, $"The picture barely changed ({changed:P1}) between 2 s and 5 s, so the video didn't play.");
    }

    [Fact]
    public void Full_screen_fills_the_display_and_esc_returns()
    {
        using var app = new Gallery("player");
        Assert.True(Within(10, () => app.Last("state") == "Playing"), "It never started playing.");
        var before = WindowRect(app.Handle);
        app.Invoke("FULL");
        var monitor = MonitorRect(app.Handle);
        Assert.True(Within(5, () => WindowRect(app.Handle).Equals(monitor)), $"Full screen is {WindowRect(app.Handle)}, the display is {monitor}.");
        app.Invoke("EXIT");
        Assert.True(Within(5, () => WindowRect(app.Handle).Equals(before)), "EXIT didn't leave full screen.");
        app.Invoke("FULL");
        Assert.True(Within(5, () => WindowRect(app.Handle).Equals(monitor)), "FULL didn't go back to full screen.");
        // A real click on the time readout, which does nothing, brings STX to the front so a real Esc reaches it.
        var time = Find(app.Window, e => System.Text.RegularExpressions.Regex.IsMatch(e.Current.Name, @"^\d\d:\d\d:\d\d$"));
        Assert.NotNull(time);
        RealClick(app, time!);
        Thread.Sleep(300);
        RealEscape(app);
        Assert.True(Within(5, () => WindowRect(app.Handle).Equals(before)), $"Esc didn't leave full screen: {WindowRect(app.Handle)}.");
    }

    [Fact]
    public void Full_screen_keeps_playing_without_a_restart()
    {
        using var app = new Gallery("player");
        Assert.True(Within(10, () => app.Number("time") > 1500), "It never played past 1.5 s.");
        double before = app.Number("time");
        app.Invoke("FULL");
        Thread.Sleep(1500);
        Assert.Equal("True", app.Last("full"));
        double after = app.Number("time");
        Assert.True(after >= before, $"The time went back from {before} ms to {after} ms, so playback restarted.");
        Assert.Equal("1", app.Last("swapchains"));
    }

    [Fact]
    public void A_real_click_on_a_pad_over_the_video_lands()
    {
        using var app = new Gallery("player-full");
        Assert.True(Within(10, () => app.Last("state") == "Playing" && app.Last("full") == "True"), "It never played in full screen.");
        // In full screen the control strip lies over the bottom of the picture.
        RealClick(app, app.Pad("PAUSE"));
        Assert.True(Within(5, () => app.Last("state") == "Paused"), $"The click on PAUSE didn't land: state {app.Last("state")}.");
        Assert.NotNull(Find(app.Window, e => e.Current.Name == "PLAY", 3));
    }

    [Fact]
    public void Full_screen_4K60_loses_under_1_percent()
    {
        string file = Output("hevc-2160p60.mkv");
        if (!File.Exists(file))
        {
            var make = Process.Start(new ProcessStartInfo("ffmpeg",
                $"-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=3840x2160:rate=60 -t 70 -c:v hevc_qsv -b:v 40M -pix_fmt nv12 \"{file}\"")
            { UseShellExecute = false })!;
            make.WaitForExit();
            Assert.True(make.ExitCode == 0 && File.Exists(file), "FFmpeg (with Intel Quick Sync) is needed to make the 4K60 HEVC test file.");
        }
        using var app = new Gallery("player-full", ("STX_PLAYER_FILE", file));
        Assert.True(Within(15, () => app.Number("time") > 5000), "It never played past 5 s.");
        double shown0 = Shown(app), lost0 = Lost(app);
        var dwm0 = Dwm();
        Thread.Sleep(60_000);
        double shown = Shown(app) - shown0, lost = Lost(app) - lost0;
        var dwm1 = Dwm();
        double dwmFrames = dwm1.Frames - dwm0.Frames, dwmDropped = (dwm1.Dropped - dwm0.Dropped) + (dwm1.Missed - dwm0.Missed);
        Assert.True(shown > 3000, $"Only {shown} pictures were shown in 60 s.");
        Assert.True(lost / (shown + lost) < 0.01, $"LibVLC lost {lost} of {shown + lost} pictures.");
        Assert.True(dwmDropped / dwmFrames < 0.01, $"The compositor dropped or missed {dwmDropped} of {dwmFrames} frames.");

        static double Shown(Gallery g) => double.Parse(g.Last("shown"), CultureInfo.InvariantCulture);
        static double Lost(Gallery g) => double.Parse(g.Last("lost"), CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Audio_reaches_the_output_device()
    {
        // Something else making sound would make the meter prove nothing, so the PC has to be quiet first.
        double quiet = PeakFor(TimeSpan.FromSeconds(1));
        Assert.True(quiet < 0.001, $"Another program is making sound (peak {quiet:F4}); close it and run again.");
        using var app = new Gallery("player-audio");
        Assert.True(Within(10, () => app.Last("state") == "Playing"), "It never started playing.");
        double peak = PeakFor(TimeSpan.FromSeconds(1.5));
        Assert.True(peak > 0.001, $"The output device's meter stayed at {peak:F4} while STX played a tone.");
    }

    [Fact]
    public void The_video_output_logs_no_errors()
    {
        using var app = new Gallery("player");
        Assert.True(Within(10, () => app.Number("time") > 2000), "It never played past 2 s.");
        string line = File.ReadAllLines(app.Log).Last();
        Assert.True(app.Last("errors") == "0", "LibVLC logged errors: " + line);
    }

    // The PERFORMANCE profile travels with each file (P1): with hardware decoding off LibVLC decodes in software,
    // and BALANCED decodes on the GPU.
    [Fact]
    public void Hardware_decoding_follows_the_profile()
    {
        using (var software = new Gallery("player", ("STX_PLAYER_PERF", "software")))
        {
            Assert.True(Within(10, () => software.Last("decoder").EndsWith("/software", StringComparison.Ordinal)), "Hardware decoding off: decoder " + software.Last("decoder"));
        }
        using var balanced = new Gallery("player", ("STX_PLAYER_PERF", "balanced"));
        Assert.True(Within(10, () => balanced.Last("decoder").EndsWith("/d3d11va", StringComparison.Ordinal)), "Balanced: decoder " + balanced.Last("decoder"));
    }

    // The default output device's peak meter, the loudest it reads over the time given.
    static double PeakFor(TimeSpan time)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var device);
        var iid = typeof(IAudioMeterInformation).GUID;
        device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var meter);
        double peak = 0;
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            ((IAudioMeterInformation)meter).GetPeakValue(out float value);
            peak = Math.Max(peak, value);
            Thread.Sleep(20);
        }
        return peak;
    }

    static (double Frames, double Dropped, double Missed) Dwm()
    {
        var info = new DWM_TIMING_INFO { Size = Marshal.SizeOf<DWM_TIMING_INFO>() };
        Marshal.ThrowExceptionForHR(DwmGetCompositionTimingInfo(IntPtr.Zero, ref info));
        return (info.Frame, info.FramesDropped, info.FramesMissed);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] record struct RECT(int Left, int Top, int Right, int Bottom);
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor; public RECT Work; public uint Flags; }

    // dwmapi.h, packed to 1. DWM_FRAME_COUNT and QPC_TIME are 64-bit.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct DWM_TIMING_INFO
    {
        public int Size;
        public UNSIGNED_RATIO RateRefresh;
        public ulong QpcRefreshPeriod;
        public UNSIGNED_RATIO RateCompose;
        public ulong QpcVBlank, Refresh;
        public uint DXRefresh;
        public ulong QpcCompose, Frame;
        public uint DXPresent;
        public ulong RefreshFrame, FrameSubmitted;
        public uint DXPresentSubmitted;
        public ulong FrameConfirmed;
        public uint DXPresentConfirmed;
        public ulong RefreshConfirmed;
        public uint DXRefreshConfirmed;
        public ulong FramesLate;
        public uint FramesOutstanding;
        public ulong FrameDisplayed, QpcFrameDisplayed, RefreshFrameDisplayed, FrameComplete, QpcFrameComplete, FramePending, QpcFramePending;
        public ulong FramesDisplayed, FramesComplete, FramesPending, FramesAvailable, FramesDropped, FramesMissed;
        public ulong RefreshNextDisplayed, RefreshNextPresented, RefreshesDisplayed, RefreshesPresented, RefreshStarted, PixelsReceived, PixelsDrawn, BuffersEmpty;
    }

    [StructLayout(LayoutKind.Sequential)] struct UNSIGNED_RATIO { public uint Numerator, Denominator; }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("dwmapi.dll")] private static extern int DwmGetCompositionTimingInfo(IntPtr window, ref DWM_TIMING_INFO info);
}
