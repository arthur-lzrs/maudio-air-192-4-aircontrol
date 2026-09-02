using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class TrimControlViewModel : ViewModelBase
{
    private readonly InputChannelId _channel;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private double _trimDb;

    public double MinDb => TrimCalculator.MinDb;
    public double MaxDb => TrimCalculator.MaxDb;

    public TrimControlViewModel(InputChannelId channel, IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _channel = channel;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = settingsRepository.Load();
        var savedSettings = channel == InputChannelId.Input1 ? profile.Input1 : profile.Input2;

        _trimDb = savedSettings.TrimDb;
        audioEngine.SetTrim(channel, savedSettings.TrimDb);
    }

    partial void OnTrimDbChanged(double value) => _audioEngine.SetTrim(_channel, value);

    /// <summary>Persiste o trim atual. Deve ser chamado ao soltar o slider, não a cada mudança contínua.</summary>
    public void CommitTrim()
    {
        var profile = _settingsRepository.Load();
        var updated = _channel == InputChannelId.Input1
            ? profile with { Input1 = profile.Input1 with { TrimDb = TrimDb } }
            : profile with { Input2 = profile.Input2 with { TrimDb = TrimDb } };
        _settingsRepository.Save(updated);
    }
}
