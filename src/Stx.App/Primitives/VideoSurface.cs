using LibVLCSharp.Platforms.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.Playback;

namespace Stx.App.Primitives;

/// <summary>
/// Where LibVLC draws: VideoLAN's VideoView, a swap chain inside the plate (spike S1). It is made once and never
/// moved to another parent, because a new parent makes a new swap chain and LibVLC can't follow it (finding F1).
/// LibVLC starts from the swap chain's options, so the engine waits for this surface before it plays (finding F2).
/// </summary>
public sealed partial class VideoSurface : Grid
{
    readonly VideoView _view = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    LibVlcEngine? _engine;
    string[]? _swapChain;

    public VideoSurface()
    {
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
        Children.Add(_view);
        _view.Initialized += (_, e) =>
        {
            Initializations++;
            _swapChain = e.SwapChainOptions;
            _engine?.UseSwapChain(_swapChain);
        };
    }

    /// <summary>How many swap chains the view has made. More than one means it was moved, and playback restarted.</summary>
    public int Initializations { get; private set; }

    /// <summary>Gives the engine this surface's swap chain, now or as soon as it exists, and shows the engine's player.</summary>
    public void Attach(LibVlcEngine engine)
    {
        Detach();
        _engine = engine;
        engine.PlayerCreated += OnPlayerCreated;
        if (_swapChain is not null) engine.UseSwapChain(_swapChain);
        ShowPlayer();
    }

    public void Detach()
    {
        if (_engine is null) return;
        _engine.PlayerCreated -= OnPlayerCreated;
        _engine = null;
        _view.MediaPlayer = null;
    }

    // The view has to hold the player before anything plays, so it can tell LibVLC its size. The swap chain's event
    // already runs on the UI thread, and the player is created inside it, so it's handed over at once.
    void OnPlayerCreated(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) ShowPlayer();
        else DispatcherQueue.TryEnqueue(ShowPlayer);
    }

    void ShowPlayer()
    {
        if (_engine?.Player is { } player && !ReferenceEquals(_view.MediaPlayer, player)) _view.MediaPlayer = player;
    }
}
