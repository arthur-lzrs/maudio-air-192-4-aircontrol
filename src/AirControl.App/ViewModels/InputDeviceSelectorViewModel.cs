using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class InputDeviceSelectorViewModel : ViewModelBase
{
    private readonly IAudioDeviceProvider _deviceProvider;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;
    private readonly string _outputDeviceId;
    private bool _isApplyingExternalChange;

    [ObservableProperty]
    private IReadOnlyList<AudioInputDeviceInfo> _availableDevices = Array.Empty<AudioInputDeviceInfo>();

    [ObservableProperty]
    private AudioInputDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private bool _needsSelection;

    public event EventHandler? ActiveDeviceChanged;

    public InputDeviceSelectorViewModel(
        IAudioDeviceProvider deviceProvider,
        IAudioEngine audioEngine,
        ISettingsRepository settingsRepository,
        string outputDeviceId)
    {
        _deviceProvider = deviceProvider;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;
        _outputDeviceId = outputDeviceId;
    }

    /// <summary>
    /// Resolve o dispositivo de entrada ativo a partir da preferência persistida (FR-011): usa a
    /// seleção manual salva se o dispositivo ainda estiver conectado; senão, auto-detecta o AIR
    /// (FR-008); se nada resolver, expõe um estado de "precisa selecionar" (FR-009) sem iniciar o
    /// engine. Não escreve <c>InputDeviceId</c> na auto-detecção (data-model.md).
    /// </summary>
    public void ResolveActiveDevice()
    {
        RefreshAvailableDevices();

        var profile = _settingsRepository.Load();
        var manualDevice = profile.InputDeviceId is not null
            ? AvailableDevices.FirstOrDefault(d => d.Id == profile.InputDeviceId)
            : null;

        if (manualDevice is not null)
        {
            StartWith(manualDevice);
            return;
        }

        var autoDevice = AvailableDevices.FirstOrDefault(d => d.IsAirDevice);
        if (autoDevice is not null)
        {
            StartWith(autoDevice);
            return;
        }

        NeedsSelection = true;
        _audioEngine.Stop();
    }

    public void RefreshAvailableDevices() => AvailableDevices = _deviceProvider.GetAvailableInputDevices();

    private void StartWith(AudioInputDeviceInfo device)
    {
        NeedsSelection = false;
        _isApplyingExternalChange = true;
        SelectedDevice = device;
        _isApplyingExternalChange = false;

        _audioEngine.Stop();
        _audioEngine.Start(device.Id, _outputDeviceId);
    }

    partial void OnSelectedDeviceChanged(AudioInputDeviceInfo? value)
    {
        if (_isApplyingExternalChange || value is null)
        {
            return;
        }

        StartWith(value);

        var profile = _settingsRepository.Load();
        _settingsRepository.Save(profile with { InputDeviceId = value.Id });

        ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
    }
}
