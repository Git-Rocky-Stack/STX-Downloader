using CommunityToolkit.Mvvm.ComponentModel;
using Stx.Look;
using Stx.Tools;

namespace Stx.ViewModels;

/// <summary>The header's indicators: are the tools ready, and is anything wrong.</summary>
public sealed class EngineStatusViewModel : ObservableObject
{
    LedColor _color = LedColor.Amber;
    LedState _state = LedState.Busy, _fault = LedState.Off;
    string _text = "CHECKING TOOLS";

    public LedColor EngineColor { get => _color; private set => SetProperty(ref _color, value); }
    public LedState EngineState { get => _state; private set => SetProperty(ref _state, value); }
    public LedState FaultState { get => _fault; private set => SetProperty(ref _fault, value); }
    public string EngineText { get => _text; private set => SetProperty(ref _text, value); }

    public void Report(ToolStatus status) => EngineText = $"{status.Tool.ToUpperInvariant()}: {status.Stage.ToUpperInvariant()}";

    public void Apply(ToolHealth health)
    {
        (EngineColor, EngineState, FaultState, EngineText) = health switch
        {
            ToolHealth.Current => (LedColor.Green, LedState.On, LedState.Off, "ENGINE CURRENT"),
            ToolHealth.UpdatePending => (LedColor.Amber, LedState.On, LedState.Off, "UPDATE WAITING"),
            ToolHealth.Degraded => (LedColor.Amber, LedState.On, LedState.Off, "LIMITED: SOME FORMATS MISSING"),
            _ => (LedColor.Red, LedState.On, LedState.On, "TOOLS MISSING"),
        };
    }
}
