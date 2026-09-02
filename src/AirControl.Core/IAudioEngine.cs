namespace AirControl.Core;

public interface IAudioEngine
{
    void Start(string outputDeviceId);
    void Stop();

    event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    void SetTrim(InputChannelId channel, double trimDb);
    void SetMute(InputChannelId channel, bool isMuted);
    void SetSolo(InputChannelId channel, bool isSoloed);

    ChannelState GetState(InputChannelId channel);

    /// <summary>
    /// Habilita/desabilita a reprodução audível (playthrough) no dispositivo de saída, sem parar
    /// a captura nem os meters. Quando desabilitada, a saída fica em silêncio independentemente
    /// de mute/solo por canal.
    /// </summary>
    bool IsMonitoringEnabled { get; }

    void SetMonitoringEnabled(bool enabled);
}
