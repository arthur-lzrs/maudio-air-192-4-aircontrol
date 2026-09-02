using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

/// <summary>
/// Controla a reprodução audível (playthrough) global, independente de mute/solo por canal e sem
/// parar a captura/meters.
/// </summary>
public partial class MonitoringViewModel : ViewModelBase
{
    private readonly IAudioEngine _audioEngine;

    [ObservableProperty]
    private bool _isMonitoringEnabled;

    public MonitoringViewModel(IAudioEngine audioEngine)
    {
        _audioEngine = audioEngine;
        _isMonitoringEnabled = audioEngine.IsMonitoringEnabled;
    }

    partial void OnIsMonitoringEnabledChanged(bool value) => _audioEngine.SetMonitoringEnabled(value);
}
