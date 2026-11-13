using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Stx.App.Primitives;

/// <summary>
/// The controls and plates a tutorial can shine its spotlight on, by key ("queue.clear", or "queue" for the plate).
/// Panels mark their own controls as they build them, so a tour points at the real thing, never a picture of it. A test
/// holds every step's key (TutorialContent) to a Mark call here in the app's source.
/// </summary>
public static class TourTargets
{
    static readonly Dictionary<string, WeakReference<FrameworkElement>> Marked = new(StringComparer.Ordinal);

    /// <summary>Marks an element as a tour target, and hands it back so a mark can sit where the element is built.</summary>
    public static T Mark<T>(T element, string key) where T : FrameworkElement
    {
        Marked[key] = new WeakReference<FrameworkElement>(element);
        return element;
    }

    /// <summary>The element marked with the key, when it still exists and is on screen in the window.</summary>
    public static FrameworkElement? Find(string key) =>
        Marked.TryGetValue(key, out var weak) && weak.TryGetTarget(out var element) && IsShown(element) ? element : null;

    /// <summary>Loaded, with a size, and neither it nor anything above it collapsed.</summary>
    public static bool IsShown(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth < 1 || element.ActualHeight < 1) return false;
        for (DependencyObject? at = element; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
}
