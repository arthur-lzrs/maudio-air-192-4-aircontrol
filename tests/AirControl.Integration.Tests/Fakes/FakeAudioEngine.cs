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

    public event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    public bool IsStarted { get; private set; }

    public string? OutputDeviceId { get; private set; }

    public string? InputDeviceId { get; private set; }

    /// <summary>Canais simulados quando nenhum dispositivo específico foi registrado via <see cref="SetChannelCountForDevice"/>.</summary>
    public int SimulatedInputChannelCount { get; set; } = 2;

    private readonly Dictionary<string, int> _channelCountByDeviceId = new();

    /// <summary>Registra a contagem de canais que <see cref="Start"/> deve simular para um dispositivo específico.</summary>
    public void SetChannelCountForDevice(string deviceId, int channelCount) => _channelCountByDeviceId[deviceId] = channelCount;

    public void Start(string? inputDeviceId, string outputDeviceId)
    {
        InputDeviceId = inputDeviceId;
        OutputDeviceId = outputDeviceId;
        IsStarted = true;
        ActiveInputChannelCount = inputDeviceId is not null && _channelCountByDeviceId.TryGetValue(inputDeviceId, out var count)
            ? count
            : SimulatedInputChannelCount;
        RoutingMode = RoutingModeApplier.ResolveFallback(RoutingMode, ActiveInputChannelCount);
        CaptureFormatDescription = "2ch, 32-bit IeeeFloat, 48000Hz (fake)";
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

        LevelsChanged?.Invoke(this, new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping));
    }

    /// <summary>
    /// Simula uma amostra por par (Input1, Input2) roteada pelo <see cref="RoutingMode"/> ativo,
    /// aplicando trim antes do roteamento (FR-006) e disparando LevelsChanged para ambos os canais
    /// com os valores já roteados (research.md §1).
    /// </summary>
    public void PushRoutedSamples(ReadOnlySpan<float> input1Raw, ReadOnlySpan<float> input2Raw)
    {
        var gain1 = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input1]);
        var gain2 = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input2]);
        var leftAudible = _toggles.IsEffectivelyAudible(InputChannelId.Input1);
        var rightAudible = _toggles.IsEffectivelyAudible(InputChannelId.Input2);

        var left = new float[input1Raw.Length];
        var right = new float[input2Raw.Length];

        for (var i = 0; i < input1Raw.Length; i++)
        {
            var input1 = leftAudible ? input1Raw[i] * gain1 : 0f;
            var input2 = rightAudible ? input2Raw[i] * gain2 : 0f;
            (left[i], right[i]) = RoutingModeApplier.Apply(RoutingMode, input1, input2);
        }

        RaiseLevels(InputChannelId.Input1, left);
        RaiseLevels(InputChannelId.Input2, right);
    }

    private void RaiseLevels(InputChannelId channel, ReadOnlySpan<float> samples)
    {
        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);
        var isClipping = LevelMetering.IsClipping(peakDb);
        LevelsChanged?.Invoke(this, new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping));
    }
}
