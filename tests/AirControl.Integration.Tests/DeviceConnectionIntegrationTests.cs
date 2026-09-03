using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

public class DeviceConnectionIntegrationTests
{
    [Fact]
    public void ConnectionChanged_TogglesStatusText()
    {
        var deviceProvider = new FakeAudioDeviceProvider();
        var statusViewModel = new DeviceStatusViewModel(deviceProvider);

        Assert.Equal("Não conectado", statusViewModel.StatusText);

        deviceProvider.SimulateConnection(true);
        Assert.Equal("Conectado", statusViewModel.StatusText);

        deviceProvider.SimulateConnection(false);
        Assert.Equal("Não conectado", statusViewModel.StatusText);
    }

    [Fact]
    public void MeterViewModel_ResetsToRestState_WhenDeviceDisconnects()
    {
        var deviceProvider = new FakeAudioDeviceProvider();
        var engine = new FakeAudioEngine();
        deviceProvider.SimulateConnection(true);
        engine.Start(null, "fake-output");

        var meterViewModel = new ChannelMeterViewModel(InputChannelId.Input1, engine, deviceProvider);
        engine.PushSamples(InputChannelId.Input1, new float[] { 0.9f });
        Assert.True(meterViewModel.PeakDb > LevelMetering.SilenceFloorDb);

        deviceProvider.SimulateConnection(false);

        Assert.Equal(LevelMetering.SilenceFloorDb, meterViewModel.PeakDb);
        Assert.False(meterViewModel.IsClipping);
        Assert.False(meterViewModel.IsDeviceConnected);
    }
}
