namespace AirControl.Core;

public record ChannelSettings(double TrimDb, bool IsMuted, bool IsSoloed)
{
    public static ChannelSettings Default { get; } = new(TrimDb: 0, IsMuted: false, IsSoloed: false);
}

public record ChannelSettingsProfile(
    ChannelSettings Input1,
    ChannelSettings Input2,
    string? OutputDeviceId)
{
    public static ChannelSettingsProfile Default { get; } = new(
        Input1: ChannelSettings.Default,
        Input2: ChannelSettings.Default,
        OutputDeviceId: null);
}
