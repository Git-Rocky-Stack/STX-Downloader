using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>Shown only when setting up the tools couldn't finish: what happened, and a way to try again.</summary>
public sealed partial class FirstRunPanel : ContentControl
{
    public FirstRunPanel(FirstRunViewModel first)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var state = new StateView { State = ViewState.Error, ActionText = "RETRY" };
        state.ActionInvoked += async (_, _) => await first.RetryCommand.ExecuteAsync(null);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Setup" });
        column.Children.Add(state);
        Content = new Faceplate { PanelName = "first-run", Padding = new Thickness(36, 28, 36, 28), Content = column };

        void Paint()
        {
            Visibility = first.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            state.State = first.IsBusy ? ViewState.Loading : ViewState.Error;
            state.Message = first.IsBusy ? "TRYING AGAIN…" : first.Message.ToUpperInvariant();
        }
        first.PropertyChanged += (_, _) => Paint();
        Paint();
    }
}
