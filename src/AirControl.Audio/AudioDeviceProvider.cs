using System.Runtime.InteropServices;
using AirControl.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AirControl.Audio;

public class AudioDeviceProvider : IAudioDeviceProvider, IMMNotificationClient, IDisposable
{
    private const string AirDeviceNameFragment = "AIR 192";

    private readonly MMDeviceEnumerator _enumerator;
    private bool _isAirDeviceConnected;
    private string? _airDeviceId;
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;

    public event EventHandler? InputDevicesChanged;

    public AudioDeviceProvider()
    {
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
        RefreshAirDeviceState();
    }

    public bool IsAirDeviceConnected => _isAirDeviceConnected;

    public IReadOnlyList<AudioOutputDeviceInfo> GetAvailableOutputDevices()
    {
        var defaultDevice = TryGetDefaultOutputDevice();
        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        return devices
            .Select(device => new AudioOutputDeviceInfo(
                device.ID,
                device.FriendlyName,
                device.ID == defaultDevice?.ID))
            .ToList();
    }

    public IReadOnlyList<AudioInputDeviceInfo> GetAvailableInputDevices()
    {
        var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

        return devices
            .Select(device => new AudioInputDeviceInfo(
                device.ID,
                device.FriendlyName,
                device.AudioClient.MixFormat.Channels,
                device.FriendlyName.Contains(AirDeviceNameFragment, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private MMDevice? TryGetDefaultOutputDevice()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void RefreshAirDeviceState()
    {
        var airDevice = _enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(device => device.FriendlyName.Contains(AirDeviceNameFragment, StringComparison.OrdinalIgnoreCase));

        var wasConnected = _isAirDeviceConnected;
        _isAirDeviceConnected = airDevice is not null;
        _airDeviceId = airDevice?.ID;

        if (wasConnected != _isAirDeviceConnected)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(_isAirDeviceConnected, _airDeviceId));
        }

        InputDevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => RefreshAirDeviceState();

    void IMMNotificationClient.OnDeviceAdded(string deviceId) => RefreshAirDeviceState();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => RefreshAirDeviceState();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
    }

    void IMMNotificationClient.OnPropertyValueChanged(string pnpDeviceId, PropertyKey key)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
        _disposed = true;
    }
}
