using AirControl.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AirControl.Audio;

public class AudioEngine : IAudioEngine, IDisposable
{
    private const string AirDeviceNameFragment = "AIR 192";
    private static readonly InputChannelId[] Channels = { InputChannelId.Input1, InputChannelId.Input2 };

    private readonly Dictionary<InputChannelId, double> _trimDb = Channels.ToDictionary(c => c, _ => 0.0);
    private readonly ChannelToggleTracker _toggles = new(Channels);

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _outputBuffer;

    public event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    public void Start(string outputDeviceId)
    {
        Stop();

        using var enumerator = new MMDeviceEnumerator();
        var inputDevice = enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(AirDeviceNameFragment, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("AIR 192|4 não encontrado entre os dispositivos de captura ativos.");

        var outputDevice = enumerator.GetDevice(outputDeviceId);

        _capture = new WasapiCapture(inputDevice);
        _capture.DataAvailable += OnDataAvailable;

        _outputBuffer = new BufferedWaveProvider(_capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
        };

        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        _output.Init(_outputBuffer);
        _output.Play();
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;

        _output?.Stop();
        _output?.Dispose();
        _output = null;

        _outputBuffer = null;
    }

    public void SetTrim(InputChannelId channel, double trimDb) => _trimDb[channel] = TrimCalculator.Clamp(trimDb);

    public void SetMute(InputChannelId channel, bool isMuted) => _toggles.SetMute(channel, isMuted);

    public void SetSolo(InputChannelId channel, bool isSoloed) => _toggles.SetSolo(channel, isSoloed);

    public ChannelState GetState(InputChannelId channel) => new(
        _trimDb[channel],
        _toggles.IsMuted(channel),
        _toggles.IsSoloed(channel),
        _toggles.IsEffectivelyAudible(channel));

    /// <summary>
    /// Assume formato IEEE float (mix format padrão do WASAPI compartilhado no Windows) para os
    /// 2 canais do AIR 192|4. Aplica trim, resolve mute/solo e calcula peak/RMS por canal.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || _outputBuffer is null)
        {
            return;
        }

        var format = _capture.WaveFormat;
        const int bytesPerSample = 4;
        var channelCount = format.Channels;
        var sampleCount = e.BytesRecorded / bytesPerSample / channelCount;

        var input1 = new float[sampleCount];
        var input2 = new float[sampleCount];
        var processed = new byte[e.BytesRecorded];

        var leftGain = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input1]);
        var rightGain = TrimCalculator.ToLinearGain(_trimDb[InputChannelId.Input2]);
        var leftAudible = _toggles.IsEffectivelyAudible(InputChannelId.Input1);
        var rightAudible = _toggles.IsEffectivelyAudible(InputChannelId.Input2);

        for (var i = 0; i < sampleCount; i++)
        {
            var frameOffset = i * channelCount * bytesPerSample;

            var leftRaw = BitConverter.ToSingle(e.Buffer, frameOffset);
            var rightRaw = channelCount > 1 ? BitConverter.ToSingle(e.Buffer, frameOffset + bytesPerSample) : leftRaw;

            input1[i] = leftRaw * leftGain;
            input2[i] = rightRaw * rightGain;

            var leftOut = leftAudible ? input1[i] : 0f;
            var rightOut = rightAudible ? input2[i] : 0f;

            BitConverter.GetBytes(leftOut).CopyTo(processed, frameOffset);
            if (channelCount > 1)
            {
                BitConverter.GetBytes(rightOut).CopyTo(processed, frameOffset + bytesPerSample);
            }
        }

        _outputBuffer.AddSamples(processed, 0, processed.Length);

        RaiseLevels(InputChannelId.Input1, input1);
        RaiseLevels(InputChannelId.Input2, input2);
    }

    private void RaiseLevels(InputChannelId channel, ReadOnlySpan<float> samples)
    {
        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);
        var isClipping = LevelMetering.IsClipping(peakDb);
        LevelsChanged?.Invoke(this, new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping));
    }

    public void Dispose() => Stop();
}
