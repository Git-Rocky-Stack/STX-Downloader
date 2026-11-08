using Microsoft.UI.Xaml.Data;
using Stx.ViewModels;
using Windows.Foundation;

namespace Stx.App.Panels;

/// <summary>
/// The Library's rows as WinUI's list pages through them: when the list nears the end of the rows read so far, it
/// asks for more (<see cref="ISupportIncrementalLoading"/>), and the Library reads the next page off the UI thread
/// (plan 2026-11-05, step 2). Only the rows scrolled to are ever held.
/// </summary>
public sealed partial class IncrementalRows : RowCollection, ISupportIncrementalLoading
{
    public bool HasMoreItems => HasMoreRows;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) =>
        System.Runtime.InteropServices.WindowsRuntime.AsyncInfo.Run(async _ =>
            new LoadMoreItemsResult { Count = (uint)await LoadMoreRowsAsync((int)Math.Min(count, int.MaxValue)) });
}
