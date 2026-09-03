using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class TrimTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(10.0, 10.0)]
    [InlineData(20.0, 10.0)]
    [InlineData(-1000.0, -1000.0)]
    [InlineData(5.5, 5.5)]
    public void Clamp_KeepsValueWithinRange(double input, double expected)
    {
        Assert.Equal(expected, TrimCalculator.Clamp(input));
    }

    [Fact]
    public void Clamp_PullsOldMaximumDownToNewMaximum()
    {
        Assert.Equal(10.0, TrimCalculator.Clamp(12.0));
    }

    [Fact]
    public void Clamp_LeavesNegativeInfinityUnchanged()
    {
        Assert.Equal(double.NegativeInfinity, TrimCalculator.Clamp(double.NegativeInfinity));
    }

    [Fact]
    public void MinDb_IsNegativeInfinity()
    {
        Assert.Equal(double.NegativeInfinity, TrimCalculator.MinDb);
    }

    [Fact]
    public void MaxDb_IsTen()
    {
        Assert.Equal(10.0, TrimCalculator.MaxDb);
    }

    [Fact]
    public void ToLinearGain_ReturnsOne_ForZeroDb()
    {
        Assert.Equal(1.0, TrimCalculator.ToLinearGain(0.0), precision: 3);
    }

    [Fact]
    public void ToLinearGain_IsGreaterThanOne_ForPositiveTrim()
    {
        Assert.True(TrimCalculator.ToLinearGain(6.0) > 1.0);
    }

    [Fact]
    public void ToLinearGain_IsLessThanOne_ForNegativeTrim()
    {
        Assert.True(TrimCalculator.ToLinearGain(-6.0) < 1.0);
    }

    [Fact]
    public void ToLinearGain_IsExactlyZero_AtMinDb()
    {
        Assert.Equal(0f, TrimCalculator.ToLinearGain(TrimCalculator.MinDb));
    }
}
