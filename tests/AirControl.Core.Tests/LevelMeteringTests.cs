using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

public class LevelMeteringTests
{
    [Fact]
    public void CalculatePeakDb_ReturnsZero_ForFullScaleSample()
    {
        var samples = new float[] { 1.0f, -0.5f, 0.2f };

        var peakDb = LevelMetering.CalculatePeakDb(samples);

        Assert.Equal(0.0, peakDb, precision: 3);
    }

    [Fact]
    public void CalculatePeakDb_ReturnsSilenceFloor_ForEmptyBuffer()
    {
        var peakDb = LevelMetering.CalculatePeakDb(ReadOnlySpan<float>.Empty);

        Assert.Equal(LevelMetering.SilenceFloorDb, peakDb);
    }

    [Fact]
    public void CalculatePeakDb_ReturnsSilenceFloor_ForSilentBuffer()
    {
        var samples = new float[] { 0f, 0f, 0f };

        var peakDb = LevelMetering.CalculatePeakDb(samples);

        Assert.Equal(LevelMetering.SilenceFloorDb, peakDb);
    }

    [Fact]
    public void CalculateRmsDb_IsLowerThanPeakDb_ForVaryingSignal()
    {
        var samples = new float[] { 1.0f, 0.0f, -1.0f, 0.0f };

        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);

        Assert.True(rmsDb < peakDb);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(-0.001, false)]
    [InlineData(-12.0, false)]
    public void IsClipping_UsesZeroDbfsThreshold(double peakDb, bool expectedClipping)
    {
        Assert.Equal(expectedClipping, LevelMetering.IsClipping(peakDb));
    }

    [Fact]
    public void CalculatePeakDb_UsesAbsoluteValue_ForNegativePeak()
    {
        var samples = new float[] { 0.1f, -0.9f, 0.2f };

        var peakDb = LevelMetering.CalculatePeakDb(samples);

        Assert.True(peakDb > -1.0);
    }

    [Fact]
    public void CalculatePeakDb_And_CalculateRmsDb_HandleSingleSampleBuffer()
    {
        var samples = new float[] { 0.5f };

        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);

        Assert.Equal(peakDb, rmsDb, precision: 3);
    }

    [Fact]
    public void CalculateRmsDb_ReturnsZero_ForConstantFullScaleSignal()
    {
        var samples = new float[] { 1.0f, 1.0f, 1.0f };

        var rmsDb = LevelMetering.CalculateRmsDb(samples);

        Assert.Equal(0.0, rmsDb, precision: 3);
    }
}
