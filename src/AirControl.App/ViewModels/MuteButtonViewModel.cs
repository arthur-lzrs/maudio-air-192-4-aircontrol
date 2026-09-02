using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class MuteButtonViewModel : ViewModelBase
{
    private readonly InputChannelId _channel;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private bool _isMuted;

    public MuteButtonViewModel(InputChannelId channel, IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _channel = channel;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = settingsRepository.Load();
        var saved = channel == InputChannelId.Input1 ? profile.Input1 : profile.Input2;

        _isMuted = saved.IsMuted;
        audioEngine.SetMute(channel, saved.IsMuted);
    }

    partial void OnIsMutedChanged(bool value)
    {
        _audioEngine.SetMute(_channel, value);

        var profile = _settingsRepository.Load();
        var updated = _channel == InputChannelId.Input1
            ? profile with { Input1 = profile.Input1 with { IsMuted = value } }
            : profile with { Input2 = profile.Input2 with { IsMuted = value } };
        _settingsRepository.Save(updated);
    }
}
