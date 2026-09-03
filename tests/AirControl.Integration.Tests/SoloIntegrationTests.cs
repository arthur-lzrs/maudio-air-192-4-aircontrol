using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

public class SoloIntegrationTests
{
    [Fact]
    public void EngagingSolo_SilencesOtherChannel_RegardlessOfItsMute()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetMute(InputChannelId.Input2, false);

        engine.SetSolo(InputChannelId.Input1, true);

        Assert.True(engine.GetState(InputChannelId.Input1).IsEffectivelyAudible);
        Assert.False(engine.GetState(InputChannelId.Input2).IsEffectivelyAudible);
    }

    [Fact]
    public void ReleasingSolo_RestoresPreviousMuteState_ForBothChannels()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetMute(InputChannelId.Input1, true);
        engine.SetMute(InputChannelId.Input2, false);

        engine.SetSolo(InputChannelId.Input2, true);
        Assert.True(engine.GetState(InputChannelId.Input2).IsEffectivelyAudible);
        Assert.False(engine.GetState(InputChannelId.Input1).IsEffectivelyAudible);

        engine.SetSolo(InputChannelId.Input2, false);

        Assert.False(engine.GetState(InputChannelId.Input1).IsEffectivelyAudible);
        Assert.True(engine.GetState(InputChannelId.Input2).IsEffectivelyAudible);
    }

    [Fact]
    public void SoloDoesNotAffectTrimOfEitherChannel()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        engine.SetTrim(InputChannelId.Input1, 4.0);
        engine.SetTrim(InputChannelId.Input2, -4.0);

        engine.SetSolo(InputChannelId.Input1, true);
        engine.SetSolo(InputChannelId.Input1, false);

        Assert.Equal(4.0, engine.GetState(InputChannelId.Input1).TrimDb);
        Assert.Equal(-4.0, engine.GetState(InputChannelId.Input2).TrimDb);
    }
}
