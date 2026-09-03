namespace AirControl.Core;

public record ChannelLevelsChangedEventArgs(
    InputChannelId Channel,
    double PeakDb,
    double RmsDb,
    bool IsClipping);

public record DeviceConnectionChangedEventArgs(bool IsConnected, string? DeviceId);

/// <summary>
/// Mudança observável do estado de saúde do fluxo de áudio
/// (contracts/audio-stream-health-contract.md). Entregue SEMPRE na thread da UI.
/// </summary>
public record AudioStreamHealthChangedEventArgs(
    AudioStreamState State,
    string? FaultReason,
    int RecoveryAttempts);
