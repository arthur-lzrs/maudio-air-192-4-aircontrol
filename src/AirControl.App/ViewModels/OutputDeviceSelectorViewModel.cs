using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirControl.App.ViewModels;

public partial class OutputDeviceSelectorViewModel : ViewModelBase
{
    private readonly ISettingsRepository _settingsRepository;

    public IReadOnlyList<AudioOutputDeviceInfo> AvailableDevices { get; }

    [ObservableProperty]
    private AudioOutputDeviceInfo? _selectedDevice;

    public event EventHandler? DeviceConfirmed;

    public OutputDeviceSelectorViewModel(IAudioDeviceProvider deviceProvider, ISettingsRepository settingsRepository)
    {
        _settingsRepository = settingsRepository;
        AvailableDevices = deviceProvider.GetAvailableOutputDevices();
        SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IsDefault) ?? AvailableDevices.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var profile = _settingsRepository.Load();
        var updated = profile with { OutputDeviceId = SelectedDevice.Id };
        _settingsRepository.Save(updated);
        DeviceConfirmed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanConfirm() => SelectedDevice is not null;

    partial void OnSelectedDeviceChanged(AudioOutputDeviceInfo? value) => ConfirmCommand.NotifyCanExecuteChanged();
}
