namespace AirControl.Core;

public record ChannelLevelsChangedEventArgs(
    InputChannelId Channel,
    double PeakDb,
    double RmsDb,
    bool IsClipping);

public record DeviceConnectionChangedEventArgs(bool IsConnected, string? DeviceId);
