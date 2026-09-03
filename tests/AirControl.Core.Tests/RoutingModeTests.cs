using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class RoutingModeTests
{
    [Fact]
    public void Apply_Stereo_RoutesInput1ToLeftAndInput2ToRight()
    {
        var (left, right) = RoutingModeApplier.Apply(RoutingMode.Stereo, 0.5f, -0.25f);

        Assert.Equal(0.5f, left);
        Assert.Equal(-0.25f, right);
    }

    [Fact]
    public void Apply_Input1Mono_DuplicatesInput1ToBothChannelsIgnoringInput2()
    {
        var (left, right) = RoutingModeApplier.Apply(RoutingMode.Input1Mono, 0.6f, 0.9f);

        Assert.Equal(0.6f, left);
        Assert.Equal(0.6f, right);
    }

    [Fact]
    public void Apply_Input2Mono_DuplicatesInput2ToBothChannelsIgnoringInput1()
    {
        var (left, right) = RoutingModeApplier.Apply(RoutingMode.Input2Mono, 0.9f, 0.4f);

        Assert.Equal(0.4f, left);
        Assert.Equal(0.4f, right);
    }

    [Fact]
    public void Apply_CombinedMono_SumsBothInputsWithMinusSixDbCompensationOnBothChannels()
    {
        var (left, right) = RoutingModeApplier.Apply(RoutingMode.CombinedMono, 1.0f, 1.0f);

        Assert.Equal(1.0f, left);
        Assert.Equal(1.0f, right);
    }

    [Fact]
    public void Apply_CombinedMono_TwoFullScaleInputsDoNotExceedFullScale()
    {
        var (left, right) = RoutingModeApplier.Apply(RoutingMode.CombinedMono, 1.0f, -1.0f);

        Assert.InRange(left, -1.0f, 1.0f);
        Assert.InRange(right, -1.0f, 1.0f);
        Assert.Equal(0f, left);
        Assert.Equal(0f, right);
    }

    [Theory]
    [InlineData(RoutingMode.Input1Mono, 1, true)]
    [InlineData(RoutingMode.Stereo, 1, false)]
    [InlineData(RoutingMode.Input2Mono, 1, false)]
    [InlineData(RoutingMode.CombinedMono, 1, false)]
    [InlineData(RoutingMode.Stereo, 2, true)]
    [InlineData(RoutingMode.Input1Mono, 2, true)]
    [InlineData(RoutingMode.Input2Mono, 2, true)]
    [InlineData(RoutingMode.CombinedMono, 2, true)]
    public void IsSupported_ReflectsChannelCountRequirement(RoutingMode mode, int channelCount, bool expected)
    {
        Assert.Equal(expected, RoutingModeApplier.IsSupported(mode, channelCount));
    }

    [Theory]
    [InlineData(RoutingMode.Stereo)]
    [InlineData(RoutingMode.Input1Mono)]
    [InlineData(RoutingMode.Input2Mono)]
    [InlineData(RoutingMode.CombinedMono)]
    public void ResolveFallback_WithOneChannel_AlwaysFallsBackToInput1Mono(RoutingMode requested)
    {
        Assert.Equal(RoutingMode.Input1Mono, RoutingModeApplier.ResolveFallback(requested, 1));
    }

    [Theory]
    [InlineData(RoutingMode.Stereo)]
    [InlineData(RoutingMode.Input1Mono)]
    [InlineData(RoutingMode.Input2Mono)]
    [InlineData(RoutingMode.CombinedMono)]
    public void ResolveFallback_WithTwoChannels_NoFallbackNeeded(RoutingMode requested)
    {
        Assert.Equal(requested, RoutingModeApplier.ResolveFallback(requested, 2));
    }
}
