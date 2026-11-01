using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>The app's current mode. Primitives redraw when it changes.</summary>
public static class LookState
{
    static LookMode _mode = LookMode.Dark;

    static readonly Windows.UI.ViewManagement.UISettings Settings = new();

    public static event Action? Changed;

    /// <summary>True when Windows is set to show no animations.</summary>
    public static bool ReduceMotion => !Settings.AnimationsEnabled;

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
