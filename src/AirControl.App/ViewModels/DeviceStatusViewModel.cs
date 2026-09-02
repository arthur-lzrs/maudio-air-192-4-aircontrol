using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class DeviceStatusViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isConnected;

    public string StatusText => IsConnected ? "Conectado" : "Não conectado";

    public DeviceStatusViewModel(IAudioDeviceProvider deviceProvider)
    {
        IsConnected = deviceProvider.IsAirDeviceConnected;
        deviceProvider.ConnectionChanged += (_, args) => IsConnected = args.IsConnected;
    }

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(StatusText));
}
