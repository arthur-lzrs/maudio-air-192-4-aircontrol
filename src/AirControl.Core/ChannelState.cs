namespace AirControl.Core;

public record ChannelState(
    double TrimDb,
    bool IsMuted,
    bool IsSoloed,
    bool IsEffectivelyAudible);
