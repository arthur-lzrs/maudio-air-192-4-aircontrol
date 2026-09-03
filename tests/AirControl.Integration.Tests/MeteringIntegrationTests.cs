using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

public class MeteringIntegrationTests
{
    [Fact]
    public void LevelsChanged_ReflectsIndependentLevelsPerChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output-device");

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushSamples(InputChannelId.Input1, new float[] { 0.9f, -0.9f });
        engine.PushSamples(InputChannelId.Input2, new float[] { 0f, 0f });

        var input1Event = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        var input2Event = Assert.Single(received, e => e.Channel == InputChannelId.Input2);

        Assert.True(input1Event.PeakDb > LevelMetering.SilenceFloorDb);
        Assert.Equal(LevelMetering.SilenceFloorDb, input2Event.PeakDb);
    }
}
