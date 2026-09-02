using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

public class MuteIntegrationTests
{
    [Fact]
    public void SetMute_SilencesOnlyTargetChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output");

        engine.SetMute(InputChannelId.Input1, true);

        Assert.False(engine.GetState(InputChannelId.Input1).IsEffectivelyAudible);
        Assert.True(engine.GetState(InputChannelId.Input2).IsEffectivelyAudible);
    }

    [Fact]
    public void SetMute_DoesNotAffectMeteringOfEitherChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start("fake-output");
        engine.SetMute(InputChannelId.Input1, true);

        ChannelLevelsChangedEventArgs? input1Levels = null;
        engine.LevelsChanged += (_, args) =>
        {
            if (args.Channel == InputChannelId.Input1)
            {
                input1Levels = args;
            }
        };

        engine.PushSamples(InputChannelId.Input1, new float[] { 0.5f });

        Assert.NotNull(input1Levels);
        Assert.True(input1Levels!.PeakDb > LevelMetering.SilenceFloorDb);
    }
}
