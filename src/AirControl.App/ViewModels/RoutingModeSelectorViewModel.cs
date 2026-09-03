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
    private IReadOnlyList<RoutingMode> _availableModes = Array.Empty<RoutingMode>();

    [ObservableProperty]
    private RoutingMode _selectedMode;

    /// <summary>
    /// False quando os canais do dispositivo ativo não são determináveis (<c>ActiveInputChannelCount == 0</c>):
    /// nesse caso <see cref="AvailableModes"/> fica vazia MAS <see cref="StatusMessage"/> traz a
    /// explicação acionável (FR-002/FR-003) — nunca um combobox vazio e silencioso.
    /// </summary>
    [ObservableProperty]
    private bool _isDeterminable;

    [ObservableProperty]
    private string? _statusMessage;

    public RoutingModeSelectorViewModel(IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = _settingsRepository.Load();
        _selectedMode = profile.RoutingMode;

        // Estado inicial derivado do engine (ainda não iniciado ⇒ não determinável + mensagem), em
        // vez de "todos os modos" otimista: assim o estado exibido é sempre honesto e o startup é
        // determinístico independente de quando a primeira notificação chega (SC-001).
        RefreshAvailableModes();
    }

    /// <summary>
    /// Aplica o modo persistido ao engine e recalcula quais modos ficam disponíveis para o
    /// dispositivo ativo. Deve ser chamado depois que <see cref="IAudioEngine.Start"/> resolveu
    /// <see cref="IAudioEngine.ActiveInputChannelCount"/> (FR-004, FR-005).
    /// </summary>
    public void ApplyPersistedMode()
    {
        RefreshAvailableModes();

        if (!IsDeterminable)
        {
            // Sem canais determináveis, aplicar o modo ao engine só faria o ResolveFallback
            // reescrever a seleção do usuário para Stereo e "perder" a preferência persistida no
            // meio de um transiente. Preserva a seleção e espera o dispositivo válido voltar
            // (FR-004) — quando voltar, esta mesma função repopula e aplica.
            return;
        }

        _audioEngine.SetRoutingMode(SelectedMode);
        SyncSelectedModeFromEngine();
    }

    /// <summary>
    /// Recalcula as opções a partir de <see cref="RoutingOptionsState"/> (lógica pura em
    /// <c>AirControl.Core</c>). Nunca deixa o combobox vazio sem uma mensagem acionável (FR-003).
    /// </summary>
    public void RefreshAvailableModes()
    {
        var state = RoutingOptionsState.Resolve(_audioEngine.ActiveInputChannelCount);
        AvailableModes = state.AvailableModes;
        IsDeterminable = state.IsDeterminable;
        StatusMessage = state.Message;
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
