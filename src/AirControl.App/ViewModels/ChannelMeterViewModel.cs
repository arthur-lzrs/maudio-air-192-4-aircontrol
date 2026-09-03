using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

public partial class ChannelMeterViewModel : ViewModelBase
{
    public InputChannelId ChannelId { get; }

    [ObservableProperty]
    private double _peakDb = LevelMetering.SilenceFloorDb;

    [ObservableProperty]
    private double _rmsDb = LevelMetering.SilenceFloorDb;

    [ObservableProperty]
    private bool _isClipping;

    [ObservableProperty]
    private bool _isDeviceConnected;

    public ChannelMeterViewModel(InputChannelId channelId, IAudioEngine audioEngine, IAudioDeviceProvider deviceProvider)
    {
        ChannelId = channelId;
        IsDeviceConnected = deviceProvider.IsAirDeviceConnected;

        // LevelsChanged já chega marshalado na thread da UI (AudioEngine/AudioDeviceProvider fazem
        // o marshalling na borda de AirControl.Audio — research.md §4 / R2); este handler só escreve
        // as propriedades observáveis.
        audioEngine.LevelsChanged += (_, args) =>
        {
            if (args.Channel != ChannelId)
            {
                return;
            }

            PeakDb = args.PeakDb;
            RmsDb = args.RmsDb;
            IsClipping = args.IsClipping;
        };

        // Contra-exemplo do contrato de saúde do fluxo: o medidor NÃO pode continuar mostrando o
        // último valor recebido depois que o fluxo parou — isso é exatamente o "medidor congelado"
        // que a US2 combate. Qualquer estado diferente de Delivering volta o medidor ao repouso.
        audioEngine.StreamHealthChanged += (_, args) =>
        {
            if (args.State != AudioStreamState.Delivering)
            {
                ResetToRestState();
            }
        };

        deviceProvider.ConnectionChanged += (_, args) =>
        {
            IsDeviceConnected = args.IsConnected;
            if (!args.IsConnected)
            {
                ResetToRestState();
            }
        };
    }

    public void ResetToRestState()
    {
        PeakDb = LevelMetering.SilenceFloorDb;
        RmsDb = LevelMetering.SilenceFloorDb;
        IsClipping = false;
    }
}
