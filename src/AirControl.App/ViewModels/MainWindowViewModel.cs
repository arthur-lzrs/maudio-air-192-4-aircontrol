using AirControl.Core;

namespace AirControl.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public DeviceStatusViewModel DeviceStatus { get; }
    public ChannelMeterViewModel Input1Meter { get; }
    public ChannelMeterViewModel Input2Meter { get; }
    public TrimControlViewModel Input1Trim { get; }
    public TrimControlViewModel Input2Trim { get; }
    public MuteButtonViewModel Input1Mute { get; }
    public MuteButtonViewModel Input2Mute { get; }
    public SoloButtonViewModel Input1Solo { get; }
    public SoloButtonViewModel Input2Solo { get; }

    public MainWindowViewModel(IAudioEngine audioEngine, IAudioDeviceProvider deviceProvider, ISettingsRepository settingsRepository)
    {
        DeviceStatus = new DeviceStatusViewModel(deviceProvider);
        Input1Meter = new ChannelMeterViewModel(InputChannelId.Input1, audioEngine, deviceProvider);
        Input2Meter = new ChannelMeterViewModel(InputChannelId.Input2, audioEngine, deviceProvider);
        Input1Trim = new TrimControlViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Trim = new TrimControlViewModel(InputChannelId.Input2, audioEngine, settingsRepository);
        Input1Mute = new MuteButtonViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Mute = new MuteButtonViewModel(InputChannelId.Input2, audioEngine, settingsRepository);
        Input1Solo = new SoloButtonViewModel(InputChannelId.Input1, audioEngine, settingsRepository);
        Input2Solo = new SoloButtonViewModel(InputChannelId.Input2, audioEngine, settingsRepository);

        deviceProvider.ConnectionChanged += (_, args) => OnConnectionChanged(args, audioEngine, settingsRepository);

        if (deviceProvider.IsAirDeviceConnected)
        {
            OnConnectionChanged(new DeviceConnectionChangedEventArgs(true, null), audioEngine, settingsRepository);
        }
    }

    private static void OnConnectionChanged(
        DeviceConnectionChangedEventArgs args,
        IAudioEngine audioEngine,
        ISettingsRepository settingsRepository)
    {
        if (!args.IsConnected)
        {
            audioEngine.Stop();
            return;
        }

        var profile = settingsRepository.Load();
        if (profile.OutputDeviceId is not null)
        {
            audioEngine.Start(profile.OutputDeviceId);
        }
    }
}
