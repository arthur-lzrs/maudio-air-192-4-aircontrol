namespace AirControl.Core;

public interface IAudioEngine
{
    /// <summary>
    /// null = auto-detectar o M-Audio AIR entre os dispositivos de captura ativos (mesmo
    /// comportamento de antes desta extensão). Se <paramref name="inputDeviceId"/> não corresponder
    /// a um dispositivo ativo, cai para a mesma auto-detecção; se nem isso resolver, lança
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    void Start(string? inputDeviceId, string outputDeviceId);
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

    /// <summary>
    /// Modo de roteamento ativo. <see cref="SetRoutingMode"/> aplica
    /// <see cref="RoutingModeApplier.ResolveFallback"/> contra <see cref="ActiveInputChannelCount"/>
    /// antes de armazenar — nunca fica em um estado inválido para o dispositivo ativo (FR-005).
    /// </summary>
    RoutingMode RoutingMode { get; }

    void SetRoutingMode(RoutingMode mode);

    /// <summary>
    /// Canais de entrada expostos pelo dispositivo de captura ativo (1 ou 2). 0 antes do primeiro
    /// <see cref="Start"/>.
    /// </summary>
    int ActiveInputChannelCount { get; }
}
