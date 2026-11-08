using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stx.Core.Library;

namespace Stx.ViewModels;

/// <summary>
/// STX as the default player. Windows doesn't let an app make itself the default, so STX registers itself for every
/// type it's proven to play, then opens Windows' Default Apps page for the user to choose it. The LED and the readout
/// show what Windows actually does, read from Windows each time, never assumed.
/// </summary>
public sealed class DefaultPlayerViewModel : ObservableObject
{
    readonly IUserRegistry _registry;
    readonly Action<string> _launch;
    readonly SettingsViewModel _settings;
    readonly string _exePath;
    readonly int _windowsBuild;
    readonly Action _associationsChanged;
    DefaultState _state = new(0, MediaTypes.All.Count);

    /// <param name="launch">Opens a link, such as Windows' Default Apps page.</param>
    /// <param name="associationsChanged">Tells Windows the registrations changed, so Explorer sees them at once.</param>
    public DefaultPlayerViewModel(IUserRegistry registry, Action<string> launch, SettingsViewModel settings, string exePath, int windowsBuild, Action? associationsChanged = null)
    {
        _registry = registry;
        _launch = launch;
        _settings = settings;
        _exePath = exePath;
        _windowsBuild = windowsBuild;
        _associationsChanged = associationsChanged ?? (() => { });
        YesCommand = new RelayCommand(() => { Choose(); _settings.MarkDefaultPlayerAsked(); OnPropertyChanged(nameof(ShowOffer)); });
        NotNowCommand = new RelayCommand(() => { _settings.MarkDefaultPlayerAsked(); OnPropertyChanged(nameof(ShowOffer)); });
        ChooseCommand = new RelayCommand(Choose);
        Refresh();
    }

    /// <summary>First-run setup's one question: shown until it's answered, and not at all if STX already is the player.</summary>
    public bool ShowOffer => !_settings.Current.DefaultPlayerAsked && !_state.IsAll;

    public string Question => "MAKE STX YOUR PLAYER FOR VIDEOS AND MUSIC?";

    public DefaultState State => _state;

    public string StateText => _state.Text;

    /// <summary>Registers STX, opens Default Apps, and asks once.</summary>
    public IRelayCommand YesCommand { get; }

    /// <summary>Never asks again. The DEFAULT PLAYER pad in Settings stays.</summary>
    public IRelayCommand NotNowCommand { get; }

    /// <summary>The DEFAULT PLAYER pad: registers STX and opens Windows' page for choosing it.</summary>
    public IRelayCommand ChooseCommand { get; }

    /// <summary>Reads again what Windows opens each type with, as when the user comes back from Default Apps.</summary>
    public void Refresh()
    {
        var state = FileAssociations.Read(_registry);
        if (state == _state) return;
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ShowOffer));
    }

    void Choose()
    {
        // The installer registers STX already. A copy run from the zip, or a registration damaged since, is put right here.
        // A policy can lock the user's file types (review 2026-11-07): the refusal used to escape the pad's command.
        // It's logged, and Windows' page still opens, since the installer's registration may well be there.
        try
        {
            if (FileAssociations.Register(_registry, _exePath)) _associationsChanged();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Stx.Core.Logging.ErrorLog.Write("default player", e);
        }
        _launch(FileAssociations.DefaultAppsLink(_windowsBuild));
    }
}
