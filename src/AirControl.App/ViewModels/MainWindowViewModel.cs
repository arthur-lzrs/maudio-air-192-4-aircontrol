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
    public RecordingFormatSelectorViewModel RecordingFormatSelector { get; }
    public DriverSettingsViewModel DriverSettings { get; }

    [ObservableProperty]
    private string? _captureFormatDescription;

    /// <summary>
    /// Estado acionável da saúde do fluxo de áudio (US2): null enquanto
    /// <see cref="AudioStreamState.Delivering"/>; mensagem transitória durante
    /// <see cref="AudioStreamState.Stalled"/> (recuperação automática em curso) e mensagem de erro
    /// acionável em <see cref="AudioStreamState.Faulted"/> (FR-007, Constitution III).
    /// </summary>
    [ObservableProperty]
    private string? _streamHealthMessage;

    public MainWindowViewModel(
        IAudioEngine audioEngine,
        IAudioDeviceProvider deviceProvider,
        ISettingsRepository settingsRepository,
        IRecordingFormatController recordingFormatController,
        IRecordingFormatRepository recordingFormatRepository,
        IAsioSampleRateController asioSampleRateController)
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
        RecordingFormatSelector = new RecordingFormatSelectorViewModel(
            recordingFormatController,
            recordingFormatRepository,
            audioEngine,
            asioSampleRateController,
            outputDeviceId);
        DriverSettings = new DriverSettingsViewModel(audioEngine, asioSampleRateController, outputDeviceId);
        // Corrige o "Formato Padrão" do Windows ANTES do próximo Start (research.md §5) — evita
        // que a primeira captura aconteça com um formato desatualizado do Windows (ex.: 44.1kHz
        // herdado do boot), que exigiria detectar e corrigir depois de já iniciado. Uma falha
        // aqui (a escrita do formato é conhecida por ser instável neste hardware) nunca pode
        // impedir o Start() que vem logo em seguida em InputDeviceSelectorViewModel.StartWith.
        InputDeviceSelector.BeforeEngineStart += (_, device) =>
        {
            try
            {
                RecordingFormatSelector.ResolveForDevice(device);
            }
            catch (Exception ex)
            {
                RecordingFormatSelector.ReportUnexpectedFailure(ex);
            }
        };
        InputDeviceSelector.ActiveDeviceChanged += (_, _) => OnActiveInputDeviceChanged();

        // O driver ASIO e o "Formato Padrão" do Windows são estados independentes neste hardware
        // (confirmado ao vivo — nem escrita nem reconexão física resincroniza um com o outro).
        // Recalcula o aviso de descasamento sempre que qualquer um dos dois lados muda.
        RecordingFormatSelector.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RecordingFormatSelectorViewModel.SelectedFormat))
            {
                DriverSettings.UpdateSampleRateMismatch(RecordingFormatSelector.SelectedFormat?.SampleRate);
            }
        };
        DriverSettings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DriverSettingsViewModel.SelectedSampleRate))
            {
                DriverSettings.UpdateSampleRateMismatch(RecordingFormatSelector.SelectedFormat?.SampleRate);
            }
        };

        audioEngine.StreamHealthChanged += (_, args) => OnStreamHealthChanged(args);

        deviceProvider.ConnectionChanged += (_, args) => OnConnectionChanged(args);
        deviceProvider.InputDevicesChanged += (_, _) => OnInputDevicesChanged();

        // Sempre tenta resolver o dispositivo de entrada no startup (auto-detectar o AIR, restaurar
        // uma seleção manual ainda conectada, ou expor o estado de "precisa selecionar"), mesmo que
        // o AIR especificamente não esteja conectado — caso contrário uma seleção manual de um
        // dispositivo não-AIR nunca seria restaurada (FR-011), e a ausência de qualquer dispositivo
        // válido nunca surfaceria o prompt de seleção no lançamento (FR-009).
        //
        // ORDEM É CONTRATO (research.md §0/R1, S6): esta resolução é DELIBERADAMENTE a última
        // instrução do construtor — depois de TODOS os handlers acima estarem fiados. O
        // AudioDeviceProvider registra o callback COM já no próprio construtor (passo 2 do §0),
        // então qualquer notificação que tenha chegado antes daqui foi perdida; re-resolver no fim
        // do ctor é o que fecha essa janela. A operação é idempotente: uma segunda notificação
        // chegando logo em seguida (ConnectionChanged/InputDevicesChanged) reexecuta exatamente o
        // mesmo caminho e produz o mesmo estado final (FR-005, SC-001) — coberto por
        // StartupDeterminismIntegrationTests.SecondNotificationRightAfterStartup_ProducesIdenticalState.
        OnConnectionChanged(new DeviceConnectionChangedEventArgs(true, null));
    }

    private void OnConnectionChanged(DeviceConnectionChangedEventArgs args)
    {
        if (!args.IsConnected)
        {
            _audioEngine.Stop();
            RoutingModeSelector.RefreshAvailableModes();
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
                RefreshDeviceDependentSections();
                CaptureFormatDescription = GetCaptureStatusDescription();
            }
            else
            {
                // Sem dispositivo resolvido, o seletor de roteamento tem que refletir isso com a
                // mensagem acionável em vez de continuar mostrando os modos da sessão anterior
                // (FR-003) — é o que torna o estado final igual em toda abertura (SC-001).
                RoutingModeSelector.RefreshAvailableModes();
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
    /// Traduz a saúde do fluxo em estado de UI (US2/FR-007). Um congelamento deixa de ser silencioso:
    /// vira mensagem transitória enquanto a recuperação automática limitada tenta restabelecer o
    /// fluxo, e mensagem de erro acionável quando as tentativas se esgotam. Voltar a
    /// <see cref="AudioStreamState.Delivering"/> limpa o estado — o app volta ao normal sozinho.
    /// O zeramento dos medidores é responsabilidade do próprio
    /// <see cref="ChannelMeterViewModel"/>, que também assina <c>StreamHealthChanged</c>.
    /// </summary>
    private void OnStreamHealthChanged(AudioStreamHealthChangedEventArgs args) =>
        StreamHealthMessage = args.State switch
        {
            AudioStreamState.Delivering => null,
            AudioStreamState.Stalled =>
                "O fluxo de áudio parou; tentando restabelecer a monitoração automaticamente…",
            AudioStreamState.Faulted => args.FaultReason,
            _ => null,
        };

    /// <summary>
    /// <see cref="IAudioEngine.Start"/> agora nunca deixa uma falha (ex.: formato não suportado,
    /// 0x88890008) escapar de <see cref="InputDeviceSelectorViewModel.StartWith"/> — isolada em
    /// <see cref="InputDeviceSelectorViewModel.StartFailure"/> para não impedir as demais seções
    /// dependentes do dispositivo de atualizar (mesma classe de bug corrigida em
    /// <see cref="RefreshDeviceDependentSections"/>). Esta função traduz esse estado para a
    /// mesma mensagem acionável que antes só aparecia quando a exceção derrubava o fluxo inteiro.
    /// </summary>
    private string? GetCaptureStatusDescription() =>
        InputDeviceSelector.StartFailure is { } ex
            ? $"Falha ao iniciar monitoração: {ex.Message}"
            : _audioEngine.CaptureFormatDescription;

    /// <summary>
    /// Após uma troca manual de dispositivo de entrada (FR-010), revalida o modo de roteamento
    /// atualmente selecionado contra a nova contagem de canais (FR-005) e atualiza o diagnóstico
    /// de formato de captura.
    /// </summary>
    private void OnActiveInputDeviceChanged()
    {
        RoutingModeSelector.ApplyPersistedMode();
        RefreshDeviceDependentSections();
        CaptureFormatDescription = GetCaptureStatusDescription();
    }

    /// <summary>
    /// Atualiza as seções que só ficam visíveis/habilitadas quando o M-Audio é o dispositivo
    /// ativo (formato de gravação, driver), reaproveitando <see cref="AudioInputDeviceInfo.IsAirDevice"/>
    /// já resolvido por <see cref="InputDeviceSelectorViewModel"/> (research.md §7).
    /// </summary>
    private void RefreshDeviceDependentSections()
    {
        // Só sincroniza a exibição aqui (nunca escreve) — a captura já está rodando neste ponto.
        // RecordingFormatSelector.ResolveForDevice (que PODE escrever no dispositivo) já rodou
        // antes do Start via InputDeviceSelector.BeforeEngineStart; chamá-lo de novo aqui, com a
        // captura ativa, podia levar a uma nova escrita sem Stop/Start ao redor — travava os
        // meters silenciosamente. Cada seção é independente: uma falha nunca pode impedir a
        // outra de atualizar (foi exatamente uma exceção não isolada que fazia o botão "Abrir
        // painel M-Audio" sumir mesmo com o dispositivo ativo e capturando sinal normalmente).
        try
        {
            RecordingFormatSelector.SyncDisplayOnly(InputDeviceSelector.SelectedDevice);
        }
        catch (Exception ex)
        {
            RecordingFormatSelector.ReportUnexpectedFailure(ex);
        }

        DriverSettings.UpdateForDevice(InputDeviceSelector.SelectedDevice);
        DriverSettings.UpdateSampleRateMismatch(RecordingFormatSelector.SelectedFormat?.SampleRate);
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
            RoutingModeSelector.RefreshAvailableModes();
            RefreshDeviceDependentSections();
            CaptureFormatDescription = null;
            return;
        }

        RoutingModeSelector.ApplyPersistedMode();
        RefreshDeviceDependentSections();
        CaptureFormatDescription = GetCaptureStatusDescription();
    }
}
