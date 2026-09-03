using AirControl.Core;

namespace AirControl.Integration.Tests.Fakes;

/// <summary>
/// Simula a captura/processamento de áudio do AIR 192|4 usando a lógica de domínio real
/// (trim/mute/solo/metering de AirControl.Core), permitindo testes de integração da UI
/// sem hardware físico (research.md §6).
/// </summary>
public class FakeAudioEngine : IAudioEngine
{
    private static readonly InputChannelId[] Channels = { InputChannelId.Input1, InputChannelId.Input2 };

    private readonly Dictionary<InputChannelId, double> _trimDb = Channels.ToDictionary(c => c, _ => 0.0);
    private readonly ChannelToggleTracker _toggles = new(Channels);
    private readonly IUiDispatcher _uiDispatcher;

    public FakeAudioEngine(IUiDispatcher? uiDispatcher = null) =>
        _uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;

    public event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    public event EventHandler<AudioStreamHealthChangedEventArgs>? StreamHealthChanged;

    public AudioStreamHealth Health { get; } = new();

    /// <summary>Relógio injetável para exercitar o watchdog sem esperar tempo real.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Quando false, cada tentativa de recuperação automática falha — leva a Faulted após o teto.</summary>
    public bool RecoveryRestartSucceeds { get; set; } = true;

    public bool IsStarted { get; private set; }

    public string? OutputDeviceId { get; private set; }

    public string? InputDeviceId { get; private set; }

    /// <summary>Canais simulados quando nenhum dispositivo específico foi registrado via <see cref="SetChannelCountForDevice"/>.</summary>
    public int SimulatedInputChannelCount { get; set; } = 2;

    private readonly Dictionary<string, int> _channelCountByDeviceId = new();

    /// <summary>Registra a contagem de canais que <see cref="Start"/> deve simular para um dispositivo específico.</summary>
    public void SetChannelCountForDevice(string deviceId, int channelCount) => _channelCountByDeviceId[deviceId] = channelCount;

    /// <summary>Quando definida, <see cref="Start"/> lança essa exceção em vez de iniciar — simula falhas reais (ex.: formato não suportado, 0x88890008).</summary>
    public Exception? ForcedStartFailure { get; set; }

    public void Start(string? inputDeviceId, string outputDeviceId)
    {
        if (ForcedStartFailure is not null)
        {
            throw ForcedStartFailure;
        }

        InputDeviceId = inputDeviceId;
        OutputDeviceId = outputDeviceId;
        IsStarted = true;
        ActiveInputChannelCount = inputDeviceId is not null && _channelCountByDeviceId.TryGetValue(inputDeviceId, out var count)
            ? count
            : SimulatedInputChannelCount;
        RoutingMode = RoutingModeApplier.ResolveFallback(RoutingMode, ActiveInputChannelCount);
        CaptureFormatDescription = "2ch, 32-bit IeeeFloat, 48000Hz (fake)";
        Health.MarkDataReceived(Clock());
    }

    public void Stop()
    {
        IsStarted = false;
        OutputDeviceId = null;
        InputDeviceId = null;
        ActiveInputChannelCount = 0;
        CaptureFormatDescription = null;
    }

    public void SetTrim(InputChannelId channel, double trimDb) => _trimDb[channel] = TrimCalculator.Clamp(trimDb);

    public void SetMute(InputChannelId channel, bool isMuted) => _toggles.SetMute(channel, isMuted);

    public void SetSolo(InputChannelId channel, bool isSoloed) => _toggles.SetSolo(channel, isSoloed);

    public ChannelState GetState(InputChannelId channel) => new(
        _trimDb[channel],
        _toggles.IsMuted(channel),
        _toggles.IsSoloed(channel),
        _toggles.IsEffectivelyAudible(channel));

    public bool IsMonitoringEnabled { get; private set; } = true;

    public void SetMonitoringEnabled(bool enabled) => IsMonitoringEnabled = enabled;

    public string? CaptureFormatDescription { get; private set; }

    public RoutingMode RoutingMode { get; private set; }

    public void SetRoutingMode(RoutingMode mode) => RoutingMode = RoutingModeApplier.ResolveFallback(mode, ActiveInputChannelCount);

    public int ActiveInputChannelCount { get; private set; }

    /// <summary>Simula um buffer capturado no canal, aplicando o trim atual e disparando LevelsChanged.</summary>
    public void PushSamples(InputChannelId channel, ReadOnlySpan<float> rawSamples)
    {
        var gain = TrimCalculator.ToLinearGain(_trimDb[channel]);
        var adjusted = new float[rawSamples.Length];
        for (var i = 0; i < rawSamples.Length; i++)
        {
            adjusted[i] = rawSamples[i] * gain;
        }

        var peakDb = LevelMetering.CalculatePeakDb(adjusted);
        var rmsDb = LevelMetering.CalculateRmsDb(adjusted);
        var isClipping = LevelMetering.IsClipping(peakDb);

        var args = new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping);
        _uiDispatcher.Post(() => LevelsChanged?.Invoke(this, args));
    }

    /// <summary>
    /// Simula um par (Input1, Input2) capturado, aplicando trim (FR-006) antes de disparar
    /// LevelsChanged. Os meters usam o sinal pré-gate/pré-roteamento — nunca zerado por mute,
    /// solo ou monitoramento desativado (research.md §1) — mesmo contrato de
    /// <see cref="AirControl.Audio.AudioEngine.OnDataAvailable"/>. O nome do método é mantido por
    /// compatibilidade com os testes existentes; "Routed" refere-se apenas ao caminho de saída
    /// audível, que este fake não expõe diretamente.
    /// </summary>
    public void PushRoutedSamples(ReadOnlySpan<float> input1Raw, ReadOnlySpan<float> input2Raw)
    {
        var gain1 = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input1]);
        var gain2 = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input2]);

        var input1 = new float[input1Raw.Length];
        var input2 = new float[input2Raw.Length];

        for (var i = 0; i < input1Raw.Length; i++)
        {
            input1[i] = input1Raw[i] * gain1;
            input2[i] = input2Raw[i] * gain2;
        }

        RaiseLevels(InputChannelId.Input1, input1);
        RaiseLevels(InputChannelId.Input2, input2);
    }

    private void RaiseLevels(InputChannelId channel, ReadOnlySpan<float> samples)
    {
        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);
        var isClipping = LevelMetering.IsClipping(peakDb);
        var args = new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping);
        _uiDispatcher.Post(() => LevelsChanged?.Invoke(this, args));
    }

    // --- Saúde do fluxo (US2) — espelha a política do AudioEngine real ---------------------------

    /// <summary>Simula a chegada de um buffer: atualiza "último dado recebido" e restaura Delivering.</summary>
    public void SimulateDataReceived()
    {
        if (Health.MarkDataReceived(Clock()))
        {
            RaiseHealthChanged();
        }
    }

    /// <summary>
    /// Simula uma parada externa do fluxo (RecordingStopped/PlaybackStopped, suspensão, perda para
    /// modo exclusivo) e a recuperação automática limitada que se segue.
    /// </summary>
    public void SimulateStreamStopped(string reason)
    {
        if (Health.MarkStalled(Clock(), reason))
        {
            RaiseHealthChanged();
        }

        RunRecovery();
    }

    /// <summary>Executa um tick do watchdog com o relógio atual (sem consultar nenhum driver).</summary>
    public void RunWatchdogTick()
    {
        if (Health.EvaluateStaleness(Clock()))
        {
            RaiseHealthChanged();
        }

        RunRecovery();
    }

    private void RunRecovery()
    {
        if (Health.State != AudioStreamState.Stalled)
        {
            return;
        }

        var inputDeviceId = InputDeviceId;
        var outputDeviceId = OutputDeviceId ?? "fake-output";

        AudioStreamRecoveryPolicy.Recover(
            Health,
            () =>
            {
                if (!RecoveryRestartSucceeds)
                {
                    throw new InvalidOperationException("dispositivo indisponível (fake)");
                }

                Stop();
                Start(inputDeviceId, outputDeviceId);
            },
            Clock,
            backoff: TimeSpan.Zero);

        RaiseHealthChanged();
    }

    private void RaiseHealthChanged()
    {
        var args = new AudioStreamHealthChangedEventArgs(Health.State, Health.FaultReason, Health.RecoveryAttempts);
        _uiDispatcher.Post(() => StreamHealthChanged?.Invoke(this, args));
    }
}
