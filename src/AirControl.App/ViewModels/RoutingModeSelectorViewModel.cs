using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class RoutingModeSelectorViewModel : ViewModelBase
{
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;
    private bool _isApplyingExternalChange;

    public IReadOnlyList<RoutingMode> AllModes { get; } = Enum.GetValues<RoutingMode>();

    [ObservableProperty]
    private IReadOnlyList<RoutingMode> _availableModes = Enum.GetValues<RoutingMode>();

    [ObservableProperty]
    private RoutingMode _selectedMode;

    public RoutingModeSelectorViewModel(IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = _settingsRepository.Load();
        _selectedMode = profile.RoutingMode;
    }

    /// <summary>
    /// Aplica o modo persistido ao engine e recalcula quais modos ficam disponíveis para o
    /// dispositivo ativo. Deve ser chamado depois que <see cref="IAudioEngine.Start"/> resolveu
    /// <see cref="IAudioEngine.ActiveInputChannelCount"/> (FR-004, FR-005).
    /// </summary>
    public void ApplyPersistedMode()
    {
        RefreshAvailableModes();
        _audioEngine.SetRoutingMode(SelectedMode);
        SyncSelectedModeFromEngine();
    }

    public void RefreshAvailableModes()
    {
        AvailableModes = AllModes
            .Where(mode => RoutingModeApplier.IsSupported(mode, _audioEngine.ActiveInputChannelCount))
            .ToList();
    }

    private void SyncSelectedModeFromEngine()
    {
        _isApplyingExternalChange = true;
        SelectedMode = _audioEngine.RoutingMode;
        _isApplyingExternalChange = false;
    }

    partial void OnSelectedModeChanged(RoutingMode value)
    {
        if (_isApplyingExternalChange)
        {
            return;
        }

        _audioEngine.SetRoutingMode(value);

        var effectiveMode = _audioEngine.RoutingMode;
        if (effectiveMode != value)
        {
            SyncSelectedModeFromEngine();
        }

        var profile = _settingsRepository.Load();
        _settingsRepository.Save(profile with { RoutingMode = effectiveMode });
    }
}
