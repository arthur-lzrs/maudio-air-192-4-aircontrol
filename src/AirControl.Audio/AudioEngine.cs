using System.Runtime.InteropServices;
using AirControl.Core;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace AirControl.Audio;

public class AudioEngine : IAudioEngine, IDisposable
{
    private const string AirDeviceNameFragment = "AIR 192";
    private static readonly InputChannelId[] Channels = { InputChannelId.Input1, InputChannelId.Input2 };

    private readonly Dictionary<InputChannelId, double> _trimDb = Channels.ToDictionary(c => c, _ => 0.0);
    private readonly ChannelToggleTracker _toggles = new(Channels);

    private static bool _mediaFoundationStarted;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _outputBuffer;
    private MediaFoundationResampler? _resampler;
    private bool _monitoringEnabled = true;
    private string? _captureFormatDescription;

    public event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    public void Start(string outputDeviceId)
    {
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var inputDevice = enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .FirstOrDefault(d => d.FriendlyName.Contains(AirDeviceNameFragment, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("AIR 192|4 não encontrado entre os dispositivos de captura ativos.");

            var outputDevice = ResolveOutputDevice(enumerator, outputDeviceId);

            _capture = CreateAndStartCapture(inputDevice);

            _outputBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
            };

            _output = CreateAndInitOutput(outputDevice, _outputBuffer);
            _output.Play();

            var format = _capture.WaveFormat;
            var channelWarning = format.Channels != 2
                ? " ⚠ esperado 2 canais (Input1/Input2 seriam duplicados ou mal mapeados)"
                : string.Empty;
            _captureFormatDescription =
                $"{format.Channels}ch, {format.BitsPerSample}-bit {format.Encoding}, {format.SampleRate}Hz{channelWarning}";
        }
        catch
        {
            // Não deixa a engine em estado parcial (ex.: captura iniciada mas saída não) se
            // qualquer etapa falhar no meio do caminho.
            Stop();
            throw;
        }
    }

    /// <summary>
    /// O dispositivo de saída salvo pode ter sido removido/desconectado (ex.: fone Bluetooth)
    /// desde a última sessão, causando AUDCLNT_E_DEVICE_INVALIDATED. Nesse caso, cai para o
    /// dispositivo de saída padrão atual em vez de propagar o erro.
    /// </summary>
    private static MMDevice ResolveOutputDevice(MMDeviceEnumerator enumerator, string outputDeviceId)
    {
        try
        {
            var device = enumerator.GetDevice(outputDeviceId);
            if (device.State == DeviceState.Active)
            {
                return device;
            }
        }
        catch (COMException)
        {
            // Dispositivo salvo não existe mais; cai para o padrão abaixo.
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    /// <summary>
    /// Algumas interfaces reportam o "mix format" compartilhado como uma struct
    /// WAVEFORMATEXTENSIBLE que o próprio driver rejeita ao inicializar o AudioClient
    /// (AUDCLNT_E_UNSUPPORTED_FORMAT), mesmo aceitando o formato IEEE float "plano" equivalente
    /// (mesmos canais/sample rate). Se a tentativa padrão falhar com esse erro, tenta de novo
    /// forçando um WaveFormat IEEE float simples com os mesmos canais/sample rate.
    /// </summary>
    private WasapiCapture CreateAndStartCapture(MMDevice inputDevice)
    {
        var capture = new WasapiCapture(inputDevice);
        capture.DataAvailable += OnDataAvailable;
        try
        {
            capture.StartRecording();
            return capture;
        }
        catch (COMException ex) when (IsUnsupportedFormat(ex))
        {
            var fallbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
            capture.DataAvailable -= OnDataAvailable;
            capture.Dispose();

            capture = new WasapiCapture(inputDevice) { WaveFormat = fallbackFormat };
            capture.DataAvailable += OnDataAvailable;
            capture.StartRecording();
            return capture;
        }
    }

    /// <summary>
    /// O formato negociado para a captura pode não ser aceito diretamente pelo dispositivo de
    /// saída escolhido (taxas/canais/bits diferentes, ou a mesma variação de struct
    /// WAVEFORMATEXTENSIBLE-vs-plana descrita em <see cref="CreateAndStartCapture"/>). Tenta
    /// inicializar direto; se falhar, reamostra para o mix format do próprio dispositivo de
    /// saída; se isso também falhar, reamostra para um WaveFormat IEEE float "plano" com os
    /// mesmos canais/sample rate do dispositivo de saída.
    /// </summary>
    private WasapiOut CreateAndInitOutput(MMDevice outputDevice, BufferedWaveProvider source)
    {
        var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        try
        {
            output.Init(source);
            return output;
        }
        catch (COMException ex) when (IsUnsupportedFormat(ex))
        {
            output.Dispose();
        }

        EnsureMediaFoundationStarted();
        var outputMixFormat = outputDevice.AudioClient.MixFormat;

        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        try
        {
            _resampler = new MediaFoundationResampler(source, outputMixFormat) { ResamplerQuality = 60 };
            output.Init(_resampler);
            return output;
        }
        catch (COMException ex) when (IsUnsupportedFormat(ex))
        {
            output.Dispose();
            _resampler?.Dispose();
        }

        var plainFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputMixFormat.SampleRate, outputMixFormat.Channels);
        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        _resampler = new MediaFoundationResampler(source, plainFormat) { ResamplerQuality = 60 };
        output.Init(_resampler);
        return output;
    }

    private static bool IsUnsupportedFormat(COMException ex) => unchecked((uint)ex.HResult) == 0x88890008;

    private static void EnsureMediaFoundationStarted()
    {
        if (!_mediaFoundationStarted)
        {
            MediaFoundationApi.Startup();
            _mediaFoundationStarted = true;
        }
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;

        _output?.Stop();
        _output?.Dispose();
        _output = null;

        _resampler?.Dispose();
        _resampler = null;

        _outputBuffer = null;
        _captureFormatDescription = null;
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

    public string? CaptureFormatDescription => _captureFormatDescription;

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
