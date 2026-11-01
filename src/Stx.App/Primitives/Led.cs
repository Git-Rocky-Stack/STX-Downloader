using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Stx.Art;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>
/// An indicator LED: chrome bezel, domed lens and bloom onto the plate. Art is drawn once and shared.
/// Strike and busy run as composition animations of opacity, off the UI thread.
/// </summary>
public sealed partial class Led : Canvas
{
    const int ArtSize = (int)LedArt.LedArtLogical * 3;

    readonly Image _housing = NewLayer(), _lit = NewLayer(), _flare = NewLayer();
    readonly Microsoft.UI.Composition.Visual _litVisual, _flareVisual;
    LedColor _color = LedColor.Green;
    LedState _state = LedState.Off;
    double? _frozen;
    int _generation;

    public Led()
    {
        Width = Height = LedArt.LedLogical;
        IsHitTestVisible = false;
        Children.Add(_housing);
        Children.Add(_lit);
        Children.Add(_flare);
        _litVisual = ElementCompositionPreview.GetElementVisual(_lit);
        _flareVisual = ElementCompositionPreview.GetElementVisual(_flare);
        _housing.Source = ArtCache.Get("led:housing", () => LedArt.Housing(ArtSize));
        ApplyArt();
        Loaded += (_, _) => { LookState.MotionChanged += OnMotionChanged; Apply(); };
        Unloaded += (_, _) => { LookState.MotionChanged -= OnMotionChanged; Stop(); };
    }

    public LedColor Color
    {
        get => _color;
        set { _color = value; ApplyArt(); }
    }

    /// <summary>Setting Strike again restarts the flash, as when a job restarts.</summary>
    public LedState State
    {
        get => _state;
        set
        {
            if (value == _state && value != LedState.Strike) return;
            _state = value;
            Apply();
        }
    }

    /// <summary>Holds one brightness and no animation, for stable screenshots.</summary>
    public double? FrozenBrightness
    {
        get => _frozen;
        set { _frozen = value; Apply(); }
    }

    static Image NewLayer()
    {
        var image = new Image { Width = LedArt.LedArtLogical, Height = LedArt.LedArtLogical, IsHitTestVisible = false };
        SetLeft(image, -LedArt.LedOverhang);
        SetTop(image, -LedArt.LedOverhang);
        return image;
    }

    void ApplyArt()
    {
        var color = _color;
        _lit.Source = ArtCache.Get($"led:lit:{color}", () => LedArt.Lit(color, ArtSize));
        _flare.Source = ArtCache.Get($"led:flare:{color}", () => LedArt.Flare(color, ArtSize));
    }

    void Apply()
    {
        Stop();
        if (_frozen is double held) { Show(held); return; }
        if (StrikeTimeline.SteadyBrightness(_state, LookState.ReduceMotion) is double steady) { Show(steady); return; }

        switch (_state)
        {
            case LedState.Strike: Show(StrikeTimeline.Default[0].Brightness); if (IsLoaded) RunStrike(); break;
            case LedState.Busy: Show(StrikeTimeline.Busy[0].Brightness); if (IsLoaded) RunBusy(); break;
        }
    }

    // Windows reports its setting from another thread.
    void OnMotionChanged() => DispatcherQueue.TryEnqueue(Apply);

    void Show(double brightness)
    {
        var (lit, flare) = StrikeTimeline.Split(brightness);
        _litVisual.Opacity = (float)lit;
        _flareVisual.Opacity = (float)flare;
    }

    void Stop()
    {
        _generation++;
        _litVisual.StopAnimation("Opacity");
        _flareVisual.StopAnimation("Opacity");
    }

    void RunStrike()
    {
        int generation = _generation;
        var compositor = _litVisual.Compositor;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Run(StrikeTimeline.Default, forever: false);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _generation) State = StrikeTimeline.After(LedState.Strike);
        });
    }

    void RunBusy() => Run(StrikeTimeline.Busy, forever: true);

    void Run(IReadOnlyList<(double T, double Brightness)> timeline, bool forever)
    {
        var compositor = _litVisual.Compositor;
        var linear = compositor.CreateLinearEasingFunction();
        var lit = compositor.CreateScalarKeyFrameAnimation();
        var flare = compositor.CreateScalarKeyFrameAnimation();
        double seconds = timeline[^1].T;

        foreach (var (t, brightness) in timeline)
        {
            var layers = StrikeTimeline.Split(brightness);
            lit.InsertKeyFrame((float)(t / seconds), (float)layers.Lit, linear);
            flare.InsertKeyFrame((float)(t / seconds), (float)layers.Flare, linear);
        }
        foreach (var animation in new[] { lit, flare })
        {
            animation.Duration = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = forever ? AnimationIterationBehavior.Forever : AnimationIterationBehavior.Count;
            if (!forever) animation.IterationCount = 1;
        }
        _litVisual.StartAnimation("Opacity", lit);
        _flareVisual.StartAnimation("Opacity", flare);
    }
}
