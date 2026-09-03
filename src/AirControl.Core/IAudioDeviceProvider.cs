namespace AirControl.Core;

public interface IAudioDeviceProvider
{
    event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    /// <summary>
    /// Disparado sempre que o conjunto de dispositivos de captura Windows ativos muda (adição,
    /// remoção ou mudança de estado de qualquer dispositivo), não só do M-Audio AIR — usado para
    /// detectar a desconexão do dispositivo de entrada manualmente selecionado (FR-012).
    /// </summary>
    event EventHandler? InputDevicesChanged;

    bool IsAirDeviceConnected { get; }

    IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices();

    IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices();
}
