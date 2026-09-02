using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class MuteSoloResolutionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Resolve_NoSolo_DependsOnlyOnOwnMute(bool isMuted, bool expectedAudible)
    {
        var channels = new Dictionary<InputChannelId, ChannelToggleState>
        {
            [InputChannelId.Input1] = new ChannelToggleState(isMuted, IsSoloed: false),
            [InputChannelId.Input2] = new ChannelToggleState(false, IsSoloed: false),
        };

        Assert.Equal(expectedAudible, EffectiveAudibilityResolver.Resolve(InputChannelId.Input1, channels));
    }

    [Fact]
    public void Resolve_SingleSolo_IsolatesSoloedChannelRegardlessOfMute()
    {
        var channels = new Dictionary<InputChannelId, ChannelToggleState>
        {
            [InputChannelId.Input1] = new ChannelToggleState(IsMuted: true, IsSoloed: true),
            [InputChannelId.Input2] = new ChannelToggleState(IsMuted: false, IsSoloed: false),
        };

        Assert.True(EffectiveAudibilityResolver.Resolve(InputChannelId.Input1, channels));
        Assert.False(EffectiveAudibilityResolver.Resolve(InputChannelId.Input2, channels));
    }

    [Fact]
    public void Resolve_AllSoloed_IsEquivalentToNoSolo()
    {
        var channels = new Dictionary<InputChannelId, ChannelToggleState>
        {
            [InputChannelId.Input1] = new ChannelToggleState(IsMuted: false, IsSoloed: true),
            [InputChannelId.Input2] = new ChannelToggleState(IsMuted: true, IsSoloed: true),
        };

        Assert.True(EffectiveAudibilityResolver.Resolve(InputChannelId.Input1, channels));
        Assert.False(EffectiveAudibilityResolver.Resolve(InputChannelId.Input2, channels));
    }

    [Fact]
    public void Tracker_SingleSolo_OverridesMuteOfSoloedChannel()
    {
        var tracker = new ChannelToggleTracker(new[] { InputChannelId.Input1, InputChannelId.Input2 });
        tracker.SetMute(InputChannelId.Input1, true);

        tracker.SetSolo(InputChannelId.Input1, true);

        Assert.True(tracker.IsEffectivelyAudible(InputChannelId.Input1));
        Assert.False(tracker.IsEffectivelyAudible(InputChannelId.Input2));
    }

    [Fact]
    public void Tracker_BothSoloed_EqualsNoSolo()
    {
        var tracker = new ChannelToggleTracker(new[] { InputChannelId.Input1, InputChannelId.Input2 });
        tracker.SetMute(InputChannelId.Input2, true);

        tracker.SetSolo(InputChannelId.Input1, true);
        tracker.SetSolo(InputChannelId.Input2, true);

        Assert.True(tracker.IsEffectivelyAudible(InputChannelId.Input1));
        Assert.False(tracker.IsEffectivelyAudible(InputChannelId.Input2));
    }

    [Fact]
    public void Tracker_ReleasingOnlySolo_RestoresPreSoloMuteState()
    {
        var tracker = new ChannelToggleTracker(new[] { InputChannelId.Input1, InputChannelId.Input2 });
        tracker.SetMute(InputChannelId.Input1, true);
        tracker.SetMute(InputChannelId.Input2, false);

        tracker.SetSolo(InputChannelId.Input1, true);
        // Enquanto soloed, o mute do próprio Input1 é ignorado.
        Assert.True(tracker.IsEffectivelyAudible(InputChannelId.Input1));

        tracker.SetSolo(InputChannelId.Input1, false);

        Assert.True(tracker.IsMuted(InputChannelId.Input1));
        Assert.False(tracker.IsMuted(InputChannelId.Input2));
        Assert.False(tracker.IsEffectivelyAudible(InputChannelId.Input1));
        Assert.True(tracker.IsEffectivelyAudible(InputChannelId.Input2));
    }
}
