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
