namespace AirControl.Core;

public interface IAudioDeviceProvider
{
    event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    bool IsAirDeviceConnected { get; }

    IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices();
}
