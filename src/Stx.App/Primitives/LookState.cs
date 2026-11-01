using Stx.Look;

namespace Stx.App.Primitives;

/// <summary>The app's current mode. Primitives redraw when it changes.</summary>
public static class LookState
{
    static LookMode _mode = LookMode.Dark;

    public static event Action? Changed;

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
