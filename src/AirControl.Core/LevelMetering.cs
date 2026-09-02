namespace AirControl.Core;

public static class LevelMetering
{
    public const double ClippingThresholdDb = 0.0;
    public const double SilenceFloorDb = -96.0;

    public static double CalculatePeakDb(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return SilenceFloorDb;
        }

        var peak = 0f;
        foreach (var sample in samples)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        return ToDb(peak);
    }

    public static double CalculateRmsDb(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return SilenceFloorDb;
        }

        var sumSquares = 0.0;
        foreach (var sample in samples)
        {
            sumSquares += (double)sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / samples.Length);
        return ToDb(rms);
    }

    public static bool IsClipping(double peakDb) => peakDb >= ClippingThresholdDb;

    private static double ToDb(double amplitude)
    {
        if (amplitude <= 0)
        {
            return SilenceFloorDb;
        }

        var db = 20 * Math.Log10(amplitude);
        return Math.Max(db, SilenceFloorDb);
    }
}
