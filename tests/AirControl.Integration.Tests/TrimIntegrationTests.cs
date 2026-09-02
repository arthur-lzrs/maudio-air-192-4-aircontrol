using System.Diagnostics;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

public class TrimIntegrationTests
{
    [Fact]
    public void SetTrim_ReflectsInLevelsChanged_Within100Ms()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output-device");

        var samples = new float[] { 0.5f, -0.5f };
        engine.PushSamples(InputChannelId.Input1, samples);

        ChannelLevelsChangedEventArgs? received = null;
        engine.LevelsChanged += (_, args) =>
        {
            if (args.Channel == InputChannelId.Input1)
            {
                received = args;
            }
        };

        var stopwatch = Stopwatch.StartNew();
        engine.SetTrim(InputChannelId.Input1, 6.0);
        engine.PushSamples(InputChannelId.Input1, samples);
        stopwatch.Stop();

        Assert.NotNull(received);
        Assert.True(stopwatch.ElapsedMilliseconds < 100);

        var baselinePeakDb = LevelMetering.CalculatePeakDb(samples);
        Assert.True(received!.PeakDb > baselinePeakDb);
    }

    [Fact]
    public void SetTrim_OnlyAffectsTargetChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output-device");
        engine.SetTrim(InputChannelId.Input1, 12.0);

        Assert.Equal(12.0, engine.GetState(InputChannelId.Input1).TrimDb);
        Assert.Equal(0.0, engine.GetState(InputChannelId.Input2).TrimDb);
    }
}
