using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>The app's current mode. Primitives redraw when it changes.</summary>
public static class LookState
{
    static LookMode _mode = LookMode.Dark;

    static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public static event Action? Changed;

    /// <summary>True when Windows is set to show no animations, or you chose that here.</summary>
    public static bool ReduceMotion => ReduceMotionChosen || !Settings.AnimationsEnabled;

    /// <summary>Set from the app's own setting, on top of the Windows one.</summary>
    public static bool ReduceMotionChosen { get; set; }

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
