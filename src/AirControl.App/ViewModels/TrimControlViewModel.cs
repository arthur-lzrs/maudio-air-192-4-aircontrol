using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirControl.App.ViewModels;

public partial class TrimControlViewModel : ViewModelBase
{
    private const double DefaultTrimDb = 0.0;

    /// <summary>
    /// Piso finito só para o slider WPF, que não aceita <see cref="double.NegativeInfinity"/>
    /// como <c>Minimum</c>. Ao chegar neste piso, o valor de domínio gravado é
    /// <see cref="double.NegativeInfinity"/>, não este número (research.md §2).
    /// </summary>
    private const double SliderFloorDbValue = -60.0;

    private readonly InputChannelId _channel;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;

    [ObservableProperty]
    private double _trimDb;

    public double MinDb => TrimCalculator.MinDb;
    public double MaxDb => TrimCalculator.MaxDb;
    public double SliderFloorDb => SliderFloorDbValue;

    public TrimControlViewModel(InputChannelId channel, IAudioEngine audioEngine, ISettingsRepository settingsRepository)
    {
        _channel = channel;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;

        var profile = settingsRepository.Load();
        var savedSettings = channel == InputChannelId.Input1 ? profile.Input1 : profile.Input2;
        var clampedTrimDb = TrimCalculator.Clamp(savedSettings.TrimDb);

        _trimDb = clampedTrimDb;
        audioEngine.SetTrim(channel, clampedTrimDb);
    }

    /// <summary>
    /// O slider não consegue expressar <see cref="double.NegativeInfinity"/> diretamente — ao
    /// atingir <see cref="SliderFloorDbValue"/>, o valor de domínio grava silêncio digital exato em
    /// vez do piso numérico (research.md §2).
    /// </summary>
    partial void OnTrimDbChanged(double value)
    {
        if (value <= SliderFloorDbValue && !double.IsNegativeInfinity(value))
        {
            TrimDb = double.NegativeInfinity;
            return;
        }

        _audioEngine.SetTrim(_channel, value);
    }

    [RelayCommand]
    private void Reset()
    {
        TrimDb = DefaultTrimDb;
        CommitTrim();
    }

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
