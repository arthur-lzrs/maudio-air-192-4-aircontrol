using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;

    public DeviceStatusViewModel DeviceStatus { get; }
    public ChannelMeterViewModel Input1Meter { get; }
    public ChannelMeterViewModel Input2Meter { get; }
    public TrimControlViewModel Input1Trim { get; }
    public TrimControlViewModel Input2Trim { get; }
    public MuteButtonViewModel Input1Mute { get; }
    public MuteButtonViewModel Input2Mute { get; }
    public SoloButtonViewModel Input1Solo { get; }
    public SoloButtonViewModel Input2Solo { get; }
    public MonitoringViewModel Monitoring { get; }
    public InputDeviceSelectorViewModel InputDeviceSelector { get; }
    public RoutingModeSelectorViewModel RoutingModeSelector { get; }

    [ObservableProperty]
    private string? _captureFormatDescription;

    public MainWindowViewModel(IAudioEngine audioEngine, IAudioDeviceProvider deviceProvider, ISettingsRepository settingsRepository)
    {
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        DeviceStatus = new DeviceStatusViewModel(deviceProvider);
        Monitoring = new MonitoringViewModel(audioEngine);
        Input1Meter = new ChannelMeterViewModel(InputChannelId.Input1, audioEngine, deviceProvider);
        Input2Meter = new ChannelMeterViewModel(InputChannelId.Input2, audioEngine, deviceProvider);
        Input1Trim = new TrimControlViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Trim = new TrimControlViewModel(InputChannelId.Input2, audioEngine, settingsRepository);
        Input1Mute = new MuteButtonViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Mute = new MuteButtonViewModel(InputChannelId.Input2, audioEngine, settingsRepository);
        Input1Solo = new SoloButtonViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Solo = new SoloButtonViewModel(InputChannelId.Input2, audioEngine, settingsRepository);
        RoutingModeSelector = new RoutingModeSelectorViewModel(audioEngine, settingsRepository);

        var outputDeviceId = settingsRepository.Load().OutputDeviceId ?? string.Empty;
        InputDeviceSelector = new InputDeviceSelectorViewModel(deviceProvider, audioEngine, settingsRepository, outputDeviceId);
        InputDeviceSelector.ActiveDeviceChanged += (_, _) => OnActiveInputDeviceChanged();

        deviceProvider.ConnectionChanged += (_, args) => OnConnectionChanged(args);
        deviceProvider.InputDevicesChanged += (_, _) => OnInputDevicesChanged();

        // Sempre tenta resolver o dispositivo de entrada no startup (auto-detectar o AIR, restaurar
        // uma seleção manual ainda conectada, ou expor o estado de "precisa selecionar"), mesmo que
        // o AIR especificamente não esteja conectado — caso contrário uma seleção manual de um
        // dispositivo não-AIR nunca seria restaurada (FR-011), e a ausência de qualquer dispositivo
        // válido nunca surfaceria o prompt de seleção no lançamento (FR-009).
        OnConnectionChanged(new DeviceConnectionChangedEventArgs(true, null));
    }

    private void OnConnectionChanged(DeviceConnectionChangedEventArgs args)
    {
        if (!args.IsConnected)
        {
            _audioEngine.Stop();
            CaptureFormatDescription = null;
            return;
        }

        var profile = _settingsRepository.Load();
        if (profile.OutputDeviceId is null)
        {
            return;
        }

        try
        {
            InputDeviceSelector.ResolveActiveDevice();
            if (!InputDeviceSelector.NeedsSelection)
            {
                RoutingModeSelector.ApplyPersistedMode();
                CaptureFormatDescription = _audioEngine.CaptureFormatDescription;
            }
        }
        catch (Exception ex)
        {
            // Uma falha ao iniciar a captura/reprodução (dispositivo desconectado, formato não
            // suportado, etc.) não deve derrubar o app inteiro — só a monitoração fica indisponível.
            CaptureFormatDescription = $"Falha ao iniciar monitoração: {ex.Message}";
        }
    }

    /// <summary>
    /// Após uma troca manual de dispositivo de entrada (FR-010), revalida o modo de roteamento
    /// atualmente selecionado contra a nova contagem de canais (FR-005) e atualiza o diagnóstico
    /// de formato de captura.
    /// </summary>
    private void OnActiveInputDeviceChanged()
    {
        RoutingModeSelector.ApplyPersistedMode();
        CaptureFormatDescription = _audioEngine.CaptureFormatDescription;
    }

    /// <summary>
    /// Reage à desconexão do dispositivo de entrada ativo (ainda que não seja o AIR) sem reiniciar
    /// a captura a cada mudança irrelevante no conjunto de dispositivos (FR-012).
    /// </summary>
    private void OnInputDevicesChanged()
    {
        InputDeviceSelector.RefreshAvailableDevices();

        var activeDevice = InputDeviceSelector.SelectedDevice;
        var activeStillPresent = activeDevice is not null
            && InputDeviceSelector.AvailableDevices.Any(d => d.Id == activeDevice.Id);

        if (activeStillPresent)
        {
            return;
        }

        InputDeviceSelector.ResolveActiveDevice();

        if (InputDeviceSelector.NeedsSelection)
        {
            CaptureFormatDescription = null;
            return;
        }

        RoutingModeSelector.ApplyPersistedMode();
        CaptureFormatDescription = _audioEngine.CaptureFormatDescription;
    }
}
