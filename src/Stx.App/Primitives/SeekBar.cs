using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Stx.Look;
using Windows.System;

namespace Stx.App.Primitives;

/// <summary>
/// The player's position, drawn as a bargraph. Click or drag along it to move; with focus, Left and Right step
/// five percent and Home and End jump to the ends. Screen readers see it as a range from 0 to 100.
/// </summary>
public sealed partial class SeekBar : ContentControl
{
    readonly Bargraph _bar = new() { Segments = 40, Zones = BargraphZones.Progress, Height = 16, VerticalAlignment = VerticalAlignment.Center };
    double _fraction;
    bool _dragging;

    public SeekBar()
    {
        // A ContentControl subclass gets no template of its own; the standard one lays the bar out across the full 44 px.
        DefaultStyleKey = typeof(ContentControl);
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Height = 44;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(this, "Position");
        // The bar is 16 px; the transparent grid around it makes all 44 px a target that takes clicks and drags.
        var target = new Grid { Height = 44, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        target.Children.Add(_bar);
        Content = target;
        PointerPressed += (_, e) => { _dragging = CapturePointer(e.Pointer); Pick(e); };
        PointerMoved += (_, e) => { if (_dragging) Pick(e); };
        PointerReleased += (_, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); };
        PointerCaptureLost += (_, _) => _dragging = false;
        KeyDown += OnKey;
    }

    /// <summary>Raised with the fraction, 0 to 1, the listener asked for.</summary>
    public event EventHandler<double>? Seeked;

    /// <summary>How far through, 0 to 1. Setting it only moves the bar.</summary>
    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Math.Clamp(double.IsNaN(value) ? 0 : value, 0, 1);
            _bar.Value = _fraction * 100;
        }
    }

    void Pick(PointerRoutedEventArgs e)
    {
        if (ActualWidth < 1) return;
        Seek(e.GetCurrentPoint(this).Position.X / ActualWidth);
        e.Handled = true;
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        double? to = e.Key switch
        {
            VirtualKey.Left => _fraction - 0.05,
            VirtualKey.Right => _fraction + 0.05,
            VirtualKey.Home => 0,
            VirtualKey.End => 1,
            _ => null,
        };
        if (to is not { } fraction) return;
        Seek(fraction);
        e.Handled = true;
    }

    internal void Seek(double fraction)
    {
        Fraction = fraction;
        Seeked?.Invoke(this, _fraction);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SeekBarPeer(this);

    sealed partial class SeekBarPeer(SeekBar owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override object GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(SeekBar);

        public double Value => owner._fraction * 100;
        public double Minimum => 0;
        public double Maximum => 100;
        public double SmallChange => 5;
        public double LargeChange => 10;
        public bool IsReadOnly => false;
        public void SetValue(double value) => owner.Seek(value / 100);
    }
}
