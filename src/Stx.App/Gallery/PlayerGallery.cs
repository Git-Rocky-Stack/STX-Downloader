using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Panels;
using Stx.Core.Playback;
using Stx.Playback;
using Stx.ViewModels;

namespace Stx.App.Gallery;

/// <summary>
/// The PLAYER plate playing a test file, for capture and for the playback tests. Nothing here reaches the library.
/// --gallery=player plays tests/fixtures/playback/moving.mp4, player-audio plays tiny-audio.flac, player-full starts
/// in full screen. --hold pauses on the first picture so a capture is the same every time.
/// Env: STX_PLAYER_FILE plays another file; STX_PLAYER_PERF = eco|balanced|max|software (hardware decoding off);
/// STX_PLAYER_THREADS sets the decoder threads; STX_PLAYER_ENHANCE = sharpen|upscale turns ENHANCE on; STX_PLAYER_LOG writes a line twice a second (state, time,
/// swap chains, decoder, pictures shown and lost, sound path, what ENHANCE VLC's log confirms, whether this GPU has
/// super resolution, LibVLC errors).
/// </summary>
internal static class PlayerGallery
{
    public static UIElement Build(Window window, GalleryOptions options)
    {
        string file = Environment.GetEnvironmentVariable("STX_PLAYER_FILE")
            ?? Fixture(options.Name == "player-audio" ? "tiny-audio.flac" : "moving.mp4");
        var hardware = new HardwareFacts(Environment.ProcessorCount, false);
        var profile = Environment.GetEnvironmentVariable("STX_PLAYER_PERF") switch
        {
            "eco" => PerformanceProfile.For(PlaybackPerformance.Eco, hardware),
            "max" => PerformanceProfile.For(PlaybackPerformance.Max, hardware),
            "software" => new PerformanceProfile(false, 2, EnhanceLevel.Off),
            _ => PerformanceProfile.For(PlaybackPerformance.Balanced, hardware),
        };
        // STX_PLAYER_THREADS sets the decoder's thread count outright, for measuring it.
        if (int.TryParse(Environment.GetEnvironmentVariable("STX_PLAYER_THREADS"), out int threads)) profile = profile with { DecoderThreads = threads };
        var engine = new LibVlcEngine(profile);
        // Set before the swap chain arrives, so the first LibVLC already has it, as the app does.
        engine.Enhance = Environment.GetEnvironmentVariable("STX_PLAYER_ENHANCE") switch
        {
            "sharpen" => EnhanceLevel.Sharpen,
            "upscale" => EnhanceLevel.Upscale,
            _ => EnhanceLevel.Off,
        };
        bool superResolution = Enhance.SuperResolution(HardwareProbe.Gpu());
        var queue = window.DispatcherQueue;
        var player = new PlayerViewModel(engine, run => queue.TryEnqueue(() => run()));
        // About the width of the rack's right column.
        var panel = new PlayerPanel(player, engine) { Margin = new Thickness(8) };
        if (options.Name != "player-full")
        {
            panel.MaxWidth = 520;
            panel.HorizontalAlignment = HorizontalAlignment.Left;
        }

        // Full screen in place, as the rack does it: the window goes full screen and the plate fills it.
        // With --hold (a capture) the window stays its gallery size, so player-full shows the full-screen look at 944x601.
        panel.FullScreenChanged += (_, on) => { if (!options.Hold) window.AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Default); };
        window.Closed += (_, _) => player.Dispose();

        // --hold opens the file paused at its start and steps one frame, so every capture shows the same first picture.
        if (options.Hold)
        {
            engine.StartPaused = true;
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            hold.Tick += (_, _) =>
            {
                if (engine.State != PlaybackState.Paused) return;
                hold.Stop();
                engine.Player?.NextFrame();
            };
            hold.Start();
        }

        panel.Loaded += (_, _) =>
        {
            player.Volume = 20;
            player.Play(file, Path.GetFileNameWithoutExtension(file), [(file, Path.GetFileNameWithoutExtension(file))]);
            if (options.Name == "player-full") player.FullScreenCommand.Execute(null);
        };

        if (Environment.GetEnvironmentVariable("STX_PLAYER_LOG") is { Length: > 0 } log)
        {
            var started = DateTime.Now;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) => File.AppendAllText(log, string.Create(CultureInfo.InvariantCulture,
                $"t={(DateTime.Now - started).TotalSeconds:F1} state={engine.State} time={engine.Position.TotalMilliseconds:F0} full={player.IsFullScreen} swapchains={panel.Surface.Initializations} decoder={engine.Decoded.Pictures}/{engine.Decoder} shown={engine.Decoded.Shown} lost={engine.Decoded.Lost} sound={engine.AudioStream} enhance={engine.EnhanceSeen?.ToString() ?? "none"} gpusuper={superResolution} window={Rect(window.AppWindow.Position.X, window.AppWindow.Position.Y, window.AppWindow.Size.Width, window.AppWindow.Size.Height)} display={Display(window)} errors={engine.LoggedErrors.Count} {string.Join(" | ", engine.LoggedErrors.TakeLast(2))}{Environment.NewLine}"));
            timer.Start();
        }

        var scroller = new ScrollViewer { Content = panel };
        return scroller;
    }

    static string Rect(int x, int y, int width, int height) => string.Create(CultureInfo.InvariantCulture, $"{width}x{height}@{x},{y}");

    static string Display(Window window)
    {
        var bounds = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
        return Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    static string Fixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "STXDownloader.sln"))) dir = dir.Parent;
        return dir is null ? name : Path.Combine(dir.FullName, "tests", "fixtures", "playback", name);
    }
}
