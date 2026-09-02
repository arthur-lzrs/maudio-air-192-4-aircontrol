using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>
/// Simula a detecção de conexão do AIR 192|4 e a lista de dispositivos de saída,
/// permitindo testes de integração sem hardware físico (research.md §6).
/// </summary>
public class FakeAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly List<AudioOutputDeviceInfo> _outputDevices = new();

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    public bool IsAirDeviceConnected { get; private set; }

    public void SetOutputDevices(IEnumerable<AudioOutputDeviceInfo> devices)
    {
        _outputDevices.Clear();
        _outputDevices.AddRange(devices);
    }

    public IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices() => _outputDevices.AsReadOnly();

    public void SimulateConnection(bool isConnected, string? deviceId = "fake-air-192-4")
    {
        IsAirDeviceConnected = isConnected;
        ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(isConnected, isConnected ? deviceId : null));
    }
}
