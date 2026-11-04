using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Look;

namespace Stx.App.Gallery;

/// <summary>
/// SPIKE S1, throwaway, never merged. LibVLC drawing inside a faceplate inside the rack's scroller.
/// Env: STX_SPIKE_MEDIA (file), STX_SPIKE_LOG (stats file), STX_SPIKE_SCRIPT=1 (scroll at 10 s, back at 15 s,
/// full screen at 20 s, out at 25 s), STX_SPIKE_SECONDS (stop and exit after, default 70).
/// </summary>
internal static class PlayerSpike
{
    static StreamWriter? _log;
    static void Log(string line)
    {
        _log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        _log?.Flush();
    }

    public static UIElement Build(Window window)
    {
        string? logPath = Environment.GetEnvironmentVariable("STX_SPIKE_LOG");
        if (logPath is not null) _log = new StreamWriter(logPath, append: false);
        string media = Environment.GetEnvironmentVariable("STX_SPIKE_MEDIA") ?? "";
        bool script = Environment.GetEnvironmentVariable("STX_SPIKE_SCRIPT") == "1";
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("STX_SPIKE_SECONDS"), out int s) ? s : 70;

        var rack = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        rack.Children.Add(Filler("above", 160));

        var view = new VideoView { Height = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
        var overPad = new Pad { Content = "OVER", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
        int overClicks = 0;
        overPad.Click += (_, _) => { overClicks++; overPad.Content = $"OVER {overClicks}"; Log($"click over-pad {overClicks}"); };
        var screen = new Grid();
        screen.Children.Add(view);
        screen.Children.Add(overPad);

        var fullPad = new Pad { Content = "FULL" };
        var nextPad = new Pad { Content = "NEXT" };
        int nextClicks = 0;
        nextPad.Click += (_, _) => { nextClicks++; nextPad.Content = $"NEXT {nextClicks}"; Log($"click next-pad {nextClicks}"); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(nextPad);
        row.Children.Add(fullPad);

        var plate = new StackPanel();
        plate.Children.Add(screen);
        plate.Children.Add(row);
        rack.Children.Add(new Faceplate { PanelName = "player-spike", Padding = new Thickness(24), Content = plate });
        rack.Children.Add(Filler("below", 600));
        var scroller = new ScrollViewer { Content = rack };

        // Full screen in place: the view never changes parent (a new parent re-creates the swap chain and LibVLC).
        // Everything else in the rack folds away, the plate loses its padding, and the view takes the window's height.
        var host = new Grid();
        host.Children.Add(scroller);
        var faceplate = (Faceplate)rack.Children[1];
        bool full = false;
        void FitFull() { if (full) view.Height = host.ActualHeight; }
        host.SizeChanged += (_, _) => FitFull();
        void ToggleFull()
        {
            full = !full;
            Log($"fullscreen {(full ? "in" : "out")}");
            foreach (var child in rack.Children) if (child != faceplate) child.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            row.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
            rack.Padding = new Thickness(full ? 0 : 8);
            faceplate.Padding = new Thickness(full ? 0 : 24);
            scroller.VerticalScrollMode = full ? ScrollMode.Disabled : ScrollMode.Enabled;
            scroller.VerticalScrollBarVisibility = full ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            scroller.ChangeView(null, 0, null, true);
            window.AppWindow.SetPresenter(full ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default);
            if (!full) view.Height = 360;
            FitFull();
        }
        fullPad.Click += (_, _) => ToggleFull();

        LibVLC? vlc = null;
        MediaPlayer? player = null;
        Media? item = null;
        int inits = 0;
        view.Initialized += (_, e) =>
        {
            inits++;
            Log($"VideoView.Initialized #{inits}");
            if (vlc is not null) { Log("Initialized fired again: a second LibVLC would be needed"); return; }
            LibVLCSharp.Shared.Core.Initialize();
            string[] options = [.. e.SwapChainOptions, "--no-lua"];
            try
            {
                vlc = new LibVLC(enableDebugLogs: true, options);
                Log("libvlc accepted options: " + string.Join(' ', options));
            }
            catch (Exception ex)
            {
                Log("libvlc REFUSED options with --no-lua: " + ex.Message);
                vlc = new LibVLC(enableDebugLogs: true, e.SwapChainOptions);
            }
            Log($"libvlc version {vlc.Version}; changeset {vlc.Changeset}; LibVLCSharp {typeof(LibVLC).Assembly.GetName().Version}");
            vlc.Log += (_, l) =>
            {
                string m = l.Message;
                if (m.Contains("va", StringComparison.OrdinalIgnoreCase) && (m.Contains("d3d11", StringComparison.OrdinalIgnoreCase) || m.Contains("dxva", StringComparison.OrdinalIgnoreCase) || m.Contains("decoder", StringComparison.OrdinalIgnoreCase))
                    || m.Contains("lua", StringComparison.OrdinalIgnoreCase) || l.Level == LogLevel.Error)
                    Log($"vlc[{l.Level}] {l.Module}: {m}");
            };
            player = new MediaPlayer(vlc);
            view.MediaPlayer = player;
            item = new Media(vlc, new Uri(media));
            player.Play(item);
            Log("play " + media);
        };

        var started = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool scrolled = false, scrolledBack = false, fullIn = false, fullOut = false;
        timer.Tick += (_, _) =>
        {
            double t = (DateTime.Now - started).TotalSeconds;
            if (item is not null && player is not null)
            {
                var st = item.Statistics;
                Log(string.Create(CultureInfo.InvariantCulture,
                    $"stats t={t:F0} pos={player.Time} decoded={st.DecodedVideo} displayed={st.DisplayedPictures} lost={st.LostPictures} demuxCorrupted={st.DemuxCorrupted} playedAudio={st.PlayedAudioBuffers} lostAudio={st.LostAudioBuffers} inputKbps={st.InputBitrate * 8000:F0}"));
            }
            var b = overPad.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, overPad.ActualWidth, overPad.ActualHeight));
            var n = nextPad.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, nextPad.ActualWidth, nextPad.ActualHeight));
            double scale = view.XamlRoot?.RasterizationScale ?? 1;
            Log(string.Create(CultureInfo.InvariantCulture, $"bounds scale={scale} over=({b.X:F0},{b.Y:F0},{b.Width:F0},{b.Height:F0}) next=({n.X:F0},{n.Y:F0},{n.Width:F0},{n.Height:F0}) scrollY={scroller.VerticalOffset:F0}"));

            // What XAML's own hit test finds at the centre of each pad: the pad (or a part of it) first means a click lands on it.
            foreach (var (name, pad, r) in new[] { ("over", overPad, b), ("next", nextPad, n) })
            {
                var centre = new Windows.Foundation.Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                var hits = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(centre, host).ToList();
                int padAt = hits.IndexOf(pad);
                int viewAt = hits.IndexOf(view);
                bool padFirst = padAt >= 0 && (viewAt < 0 || padAt < viewAt);
                Log($"hittest {name} at ({centre.X:F0},{centre.Y:F0}) inWindow={centre.Y >= 0 && centre.Y < host.ActualHeight} pad@{padAt} view@{viewAt} padAboveView={padFirst} visible={pad.Visibility}");
            }
            if (Environment.GetEnvironmentVariable("STX_SPIKE_FULLSTART") == "1" && t >= 2 && !full && !fullIn) { fullIn = true; ToggleFull(); }
            if (script)
            {
                if (t >= 10 && !scrolled) { scrolled = true; scroller.ChangeView(null, 300, null, true); Log("scroll to 300"); }
                if (t >= 15 && !scrolledBack) { scrolledBack = true; scroller.ChangeView(null, 0, null, true); Log("scroll to 0"); }
                if (t >= 20 && !fullIn) { fullIn = true; ToggleFull(); }
                if (t >= 25 && !fullOut) { fullOut = true; ToggleFull(); }
            }
            if (t >= seconds)
            {
                timer.Stop();
                Log("done");
                player?.Stop();
                _log?.Dispose();
                Application.Current.Exit();
            }
        };
        timer.Start();
        return host;
    }

    static Faceplate Filler(string name, double height) =>
        new() { PanelName = name, Padding = new Thickness(24), Content = new EngravedLabel { Kind = LabelKind.Title, Text = name.ToUpperInvariant(), Height = height } };
}
