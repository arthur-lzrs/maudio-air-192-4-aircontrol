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

    [Fact]
    public void LevelsChanged_KeepsMeteringWhenMonitoringDisabled()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output-device");
        engine.SetMonitoringEnabled(false);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0.9f, -0.9f }, new float[] { 0f, 0f });

        var input1Event = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        Assert.True(input1Event.PeakDb > LevelMetering.SilenceFloorDb);
    }

    [Fact]
    public void LevelsChanged_KeepsMeteringWhenChannelIsMuted()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output-device");
        engine.SetMute(InputChannelId.Input1, true);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0.9f, -0.9f }, new float[] { 0f, 0f });

        var input1Event = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        Assert.True(input1Event.PeakDb > LevelMetering.SilenceFloorDb);
    }

    [Fact]
    public void LevelsChanged_KeepsMeteringForNonSoloedChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output-device");
        engine.SetSolo(InputChannelId.Input2, true);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 0.9f, -0.9f }, new float[] { 0f, 0f });

        var input1Event = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        Assert.True(input1Event.PeakDb > LevelMetering.SilenceFloorDb);
    }

    [Fact]
    public void LevelsChanged_ClippingIndicatorStillActivatesWhenSilencedFromOutput()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output-device");
        engine.SetMonitoringEnabled(false);
        engine.SetMute(InputChannelId.Input1, true);
        engine.SetSolo(InputChannelId.Input2, true);

        var received = new List<ChannelLevelsChangedEventArgs>();
        engine.LevelsChanged += (_, args) => received.Add(args);

        engine.PushRoutedSamples(new float[] { 1f, -1f }, new float[] { 0f, 0f });

        var input1Event = Assert.Single(received, e => e.Channel == InputChannelId.Input1);
        Assert.True(input1Event.IsClipping);
    }
}
