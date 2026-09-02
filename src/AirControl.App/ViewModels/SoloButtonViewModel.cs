using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class SoloButtonViewModel : ViewModelBase
{
    private readonly InputChannelId _channel;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private bool _isSoloed;

    public SoloButtonViewModel(InputChannelId channel, IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _channel = channel;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = settingsRepository.Load();
        var saved = channel == InputChannelId.Input1 ? profile.Input1 : profile.Input2;

        _isSoloed = saved.IsSoloed;
        audioEngine.SetSolo(channel, saved.IsSoloed);
    }

    partial void OnIsSoloedChanged(bool value)
    {
        _audioEngine.SetSolo(_channel, value);

        var profile = _settingsRepository.Load();
        var updated = _channel == InputChannelId.Input1
            ? profile with { Input1 = profile.Input1 with { IsSoloed = value } }
            : profile with { Input2 = profile.Input2 with { IsSoloed = value } };
        _settingsRepository.Save(updated);
    }
}
