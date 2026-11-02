using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Stx.Look;
using Windows.Foundation;

namespace Stx.App.Primitives;

/// <summary>
/// A line of display text that scrolls when it's too long for its screen. The compositor moves it, so nothing is
/// redrawn and the glow travels with the text.
/// </summary>
public sealed partial class ScrollingGlowText : Grid
{
    /// <summary>Up and down, the glow fades out before this. Sideways, this reaches the edge of the screen behind the text.</summary>
    const double GlowRoom = 18, ScreenEdge = 6;

    readonly GlowText _text = new();
    readonly RectangleGeometry _clip = new();
    bool _working, _lookedAt, _running;
    double _runningOverflow;

    public ScrollingGlowText()
    {
        // A canvas measures its child at full length. In a grid the text would be cut to the screen's width.
        var track = new Canvas();
        track.Children.Add(_text);
        Children.Add(track);
        Clip = _clip;
        Height = 20;

        ElementCompositionPreview.SetIsTranslationEnabled(_text, true);
        SizeChanged += (_, _) => Fit();
        _text.SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => { LookState.Changed += Update; Update(); };
        Unloaded += (_, _) => { LookState.Changed -= Update; Stop(); };
    }

    public string Text
    {
        get => _text.Text;
        set => _text.Text = value;
    }

    public DisplayTone Tone
    {
        get => _text.Tone;
        set => _text.Tone = value;
    }

    /// <summary>The job is doing something, so its line keeps moving.</summary>
    public bool Working
    {
        get => _working;
        set { if (_working != value) { _working = value; Restart(); } }
    }

    /// <summary>Selected or under the pointer. Becoming so plays its passes from the start.</summary>
    public bool LookedAt
    {
        get => _lookedAt;
        set { if (_lookedAt != value) { _lookedAt = value; Restart(); } }
    }

    double Overflow => _text.ActualWidth - ActualWidth;

    void Fit()
    {
        if (_text.ActualHeight > 0) Height = _text.ActualHeight;
        _clip.Rect = new Rect(-ScreenEdge, -GlowRoom, Math.Max(0, ActualWidth) + ScreenEdge * 2, Height + GlowRoom * 2);
        Update();
    }

    void Restart()
    {
        Stop();
        Update();
    }

    void Update()
    {
        double overflow = Overflow;
        bool wanted = IsLoaded && MarqueeTimeline.ShouldScroll(overflow, _working, _lookedAt, LookState.ReduceMotion);
        if (!wanted) { Stop(); return; }
        if (_running && overflow == _runningOverflow) return;
        Start(overflow);
    }

    void Start(double overflow)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_text);
        var compositor = visual.Compositor;
        var (travelStarts, travelEnds) = MarqueeTimeline.Fractions(overflow);
        var pass = compositor.CreateScalarKeyFrameAnimation();
        var linear = compositor.CreateLinearEasingFunction();
        pass.InsertKeyFrame(0f, 0f, linear);
        pass.InsertKeyFrame((float)travelStarts, 0f, linear);
        pass.InsertKeyFrame((float)travelEnds, (float)-overflow, linear);
        pass.InsertKeyFrame(0.9999f, (float)-overflow, linear);
        pass.InsertKeyFrame(1f, 0f, compositor.CreateStepEasingFunction());
        pass.Duration = TimeSpan.FromSeconds(MarqueeTimeline.PassSeconds(overflow));
        if (_working) pass.IterationBehavior = AnimationIterationBehavior.Forever;
        else
        {
            pass.IterationBehavior = AnimationIterationBehavior.Count;
            pass.IterationCount = MarqueeTimeline.PassesWhileLookedAt;
        }
        visual.StartAnimation("Translation.X", pass);
        _running = true;
        _runningOverflow = overflow;
    }

    void Stop()
    {
        if (!_running) return;
        var visual = ElementCompositionPreview.GetElementVisual(_text);
        visual.StopAnimation("Translation.X");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        _running = false;
    }
}
