using Stx.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Stx.App.Platform;

/// <summary>Listens for clipboard changes only while the setting is on. It's an event, so there's no polling and nothing runs between copies.</summary>
public sealed class ClipboardWatcher(ClipboardOfferViewModel offer) : IDisposable
{
    bool _listening;

    public void SetEnabled(bool enabled)
    {
        if (enabled == _listening) return;
        _listening = enabled;
        if (enabled) Clipboard.ContentChanged += OnChanged;
        else Clipboard.ContentChanged -= OnChanged;
    }

    public void Dispose() => SetEnabled(false);

    async void OnChanged(object? sender, object e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text)) offer.OnClipboardText(await content.GetTextAsync());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // Another program is holding the clipboard. This copy simply isn't offered.
        }
    }
}
