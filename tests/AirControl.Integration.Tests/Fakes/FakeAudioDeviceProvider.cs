using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>
/// Simula a detecção de conexão do AIR 192|4 e a lista de dispositivos de saída,
/// permitindo testes de integração sem hardware físico (research.md §6).
/// </summary>
public class FakeAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly List<AudioOutputDeviceInfo> _outputDevices = new();
    private readonly List<AudioInputDeviceInfo> _inputDevices = new();

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler? InputDevicesChanged;

    public bool IsAirDeviceConnected { get; private set; }

    public void SetOutputDevices(IEnumerable<AudioOutputDeviceInfo> devices)
    {
        _outputDevices.Clear();
        _outputDevices.AddRange(devices);
    }

    public IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices() => _outputDevices.AsReadOnly();

    public void SetInputDevices(IEnumerable<AudioInputDeviceInfo> devices)
    {
        _inputDevices.Clear();
        _inputDevices.AddRange(devices);
    }

    public IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices() => _inputDevices.AsReadOnly();

    /// <summary>Simula uma mudança no conjunto de dispositivos de entrada ativos (ex.: desconexão de um dispositivo selecionado manualmente).</summary>
    public void SimulateInputDevicesChanged(IEnumerable<AudioInputDeviceInfo>? updatedDevices = null)
    {
        if (updatedDevices is not null)
        {
            SetInputDevices(updatedDevices);
        }

        InputDevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateConnection(bool isConnected, string? deviceId = "fake-air-192-4")
    {
        IsAirDeviceConnected = isConnected;
        ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(isConnected, isConnected ? deviceId : null));
    }
}
