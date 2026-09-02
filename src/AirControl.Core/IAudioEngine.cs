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

    /// <summary>
    /// Descrição diagnóstica do formato de captura negociado (canais, bits, encoding, sample
    /// rate), populada após <see cref="Start"/>. Útil para diagnosticar se o formato real do
    /// dispositivo é o esperado (ex.: descartar vazamento entre canais causado por um formato
    /// inesperado). Nula antes do primeiro <see cref="Start"/>.
    /// </summary>
    string? CaptureFormatDescription { get; }
}
