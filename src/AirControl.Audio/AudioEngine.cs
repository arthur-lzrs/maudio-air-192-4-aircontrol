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
    private bool _monitoringEnabled = true;

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

    public bool IsMonitoringEnabled => _monitoringEnabled;

    public void SetMonitoringEnabled(bool enabled) => _monitoringEnabled = enabled;

    /// <summary>
    /// Lê o formato de amostra real do dispositivo (bits/encoding), em vez de assumir float de
    /// 32 bits: o mix format compartilhado do WASAPI para o AIR 192|4 é tipicamente PCM de 16 ou
    /// 24 bits, não IEEE float, e assumir 4 bytes/amostra desalinha a leitura entre os 2 canais,
    /// fazendo o Input 2 "ler" bytes do Input 1 como ruído. Aplica trim, resolve mute/solo e
    /// calcula peak/RMS por canal.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || _outputBuffer is null)
        {
            return;
        }

        var format = _capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
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

            var leftRaw = SampleFormatIO.ReadSample(e.Buffer, frameOffset, format);
            var rightRaw = channelCount > 1
                ? SampleFormatIO.ReadSample(e.Buffer, frameOffset + bytesPerSample, format)
                : leftRaw;

            input1[i] = leftRaw * leftGain;
            input2[i] = rightRaw * rightGain;

            var leftOut = leftAudible && _monitoringEnabled ? input1[i] : 0f;
            var rightOut = rightAudible && _monitoringEnabled ? input2[i] : 0f;

            SampleFormatIO.WriteSample(processed, frameOffset, leftOut, format);
            if (channelCount > 1)
            {
                SampleFormatIO.WriteSample(processed, frameOffset + bytesPerSample, rightOut, format);
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
