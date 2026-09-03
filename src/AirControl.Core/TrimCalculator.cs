namespace AirControl.Core;

public static class TrimCalculator
{
    public const double MinDb = double.NegativeInfinity;
    public const double MaxDb = 10.0;

    public static double Clamp(double trimDb) => Math.Clamp(trimDb, MinDb, MaxDb);

    public static float ToLinearGain(double trimDb) => (float)Math.Pow(10, Clamp(trimDb) / 20.0);
}
