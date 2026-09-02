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

    public void Start(string outputDeviceId)
    {
        OutputDeviceId = outputDeviceId;
        IsStarted = true;
    }

    public void Stop()
    {
        IsStarted = false;
        OutputDeviceId = null;
    }

    public void SetTrim(InputChannelId channel, double trimDb) => _trimDb[channel] = TrimCalculator.Clamp(trimDb);

    public void SetMute(InputChannelId channel, bool isMuted) => _toggles.SetMute(channel, isMuted);

    public void SetSolo(InputChannelId channel, bool isSoloed) => _toggles.SetSolo(channel, isSoloed);

    public ChannelState GetState(InputChannelId channel) => new(
        _trimDb[channel],
        _toggles.IsMuted(channel),
        _toggles.IsSoloed(channel),
        _toggles.IsEffectivelyAudible(channel));

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
}
