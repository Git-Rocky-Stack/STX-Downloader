using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>The app's current mode. Primitives redraw when it changes.</summary>
public static class LookState
{
    static LookMode _mode = LookMode.Dark;

    static readonly Windows.UI.ViewManagement.UISettings Settings = CreateSettings();
    static bool _reduceMotionChosen;

    public static event Action? Changed;

    /// <summary>Fires when reduced motion turns on or off, here or in Windows, possibly on another thread.</summary>
    public static event Action? MotionChanged;

    /// <summary>True when Windows is set to show no animations, or you chose that here.</summary>
    public static bool ReduceMotion => ReduceMotionChosen || !Settings.AnimationsEnabled;

    /// <summary>Set from the app's own setting, on top of the Windows one.</summary>
    public static bool ReduceMotionChosen
    {
        get => _reduceMotionChosen;
        set
        {
            if (_reduceMotionChosen == value) return;
            _reduceMotionChosen = value;
            MotionChanged?.Invoke();
        }
    }

    static Windows.UI.ViewManagement.UISettings CreateSettings()
    {
        var settings = new Windows.UI.ViewManagement.UISettings();
        settings.AnimationsEnabledChanged += (_, _) => MotionChanged?.Invoke();
        return settings;
    }

    public static LookMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Changed?.Invoke();
        }
    }
}
