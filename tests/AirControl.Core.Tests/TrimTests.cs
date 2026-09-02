using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class TrimTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(12.0, 12.0)]
    [InlineData(-12.0, -12.0)]
    [InlineData(20.0, 12.0)]
    [InlineData(-20.0, -12.0)]
    [InlineData(5.5, 5.5)]
    public void Clamp_KeepsValueWithinRange(double input, double expected)
    {
        Assert.Equal(expected, TrimCalculator.Clamp(input));
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
}
