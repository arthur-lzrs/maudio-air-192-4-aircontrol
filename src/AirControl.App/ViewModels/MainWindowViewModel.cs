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

        deviceProvider.ConnectionChanged += (_, args) => OnConnectionChanged(args);

        if (deviceProvider.IsAirDeviceConnected)
        {
            OnConnectionChanged(new DeviceConnectionChangedEventArgs(true, null));
        }
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
            _audioEngine.Start(profile.OutputDeviceId);
            CaptureFormatDescription = _audioEngine.CaptureFormatDescription;
        }
        catch (Exception ex)
        {
            // Uma falha ao iniciar a captura/reprodução (dispositivo desconectado, formato não
            // suportado, etc.) não deve derrubar o app inteiro — só a monitoração fica indisponível.
            CaptureFormatDescription = $"Falha ao iniciar monitoração: {ex.Message}";
        }
    }
}
