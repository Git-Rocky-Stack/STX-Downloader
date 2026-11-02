using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>Loading, error, empty and disabled, shown the same way everywhere: an indicator LED and a line on a display.</summary>
public sealed partial class StateView : Grid
{
    readonly Display _display = new() { Height = 56 };
    readonly Led _led = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly GlowText _text = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Pad _action = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    ViewState _state = ViewState.Empty;
    bool _still;

    public StateView()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        line.Children.Add(_led);
        line.Children.Add(_text);
        _display.Content = line;
        Children.Add(_display);

        SetColumn(_action, 1);
        _action.Click += (_, _) => ActionInvoked?.Invoke(this, EventArgs.Empty);
        Children.Add(_action);

        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Paint();
    }

    /// <summary>The way forward from an error, such as Retry.</summary>
    public event EventHandler? ActionInvoked;

    public ViewState State
    {
        get => _state;
        set { _state = value; Paint(); }
    }

    public string Message
    {
        get => _text.Text;
        set { _text.Text = value; AutomationProperties.SetName(this, value); }
    }

    public string ActionText
    {
        get => _action.Content as string ?? "";
        set { _action.Content = value; Paint(); }
    }

    /// <summary>Holds the LED steady, for stable screenshots.</summary>
    public bool HoldStill
    {
        get => _still;
        set { _still = value; Paint(); }
    }

    void Paint()
    {
        var look = StateLook.For(_state);
        var tone = _state == ViewState.Error || _state == ViewState.Loading ? DisplayTone.Amber : DisplayTone.Cyan;
        _display.Tone = tone;
        _text.Tone = tone;
        _text.Dimmed = look.Dimmed;
        _led.Color = look.Color;
        _led.State = look.Led;
        _led.FrozenBrightness = _still && look.Led == LedState.Busy ? 1 : null;
        _action.Visibility = look.OffersAction && !string.IsNullOrEmpty(ActionText) ? Visibility.Visible : Visibility.Collapsed;
        ColumnSpacing = _action.Visibility == Visibility.Visible ? 16 : 0;
        Opacity = _state == ViewState.Disabled ? 0.6 : 1;
    }
}
