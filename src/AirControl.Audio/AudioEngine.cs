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
    private readonly IUiDispatcher _uiDispatcher;

    private readonly object _pendingLevelsLock = new();
    private readonly Dictionary<InputChannelId, ChannelLevelsChangedEventArgs> _pendingLevels = new();
    private bool _levelsDispatchScheduled;

    private static bool _mediaFoundationStarted;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _outputBuffer;
    private MediaFoundationResampler? _resampler;
    private bool _monitoringEnabled = true;
    private string? _captureFormatDescription;
    private RoutingMode _routingMode;
    private int _activeInputChannelCount;

    private readonly AudioStreamHealth _health = new();
    private System.Threading.Timer? _watchdog;
    private long _lastDataTicks;
    private string? _activeInputDeviceId;
    private string? _activeOutputDeviceId;
    private bool _isRecovering;

    /// <summary>Cadência do watchdog: 1s, bem abaixo do limiar de 5s de staleness (SC-002).</summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(1);

    public event EventHandler<ChannelLevelsChangedEventArgs>? LevelsChanged;

    public event EventHandler<AudioStreamHealthChangedEventArgs>? StreamHealthChanged;

    public AudioStreamHealth Health => _health;

    /// <param name="uiDispatcher">
    /// Marshalling de <see cref="LevelsChanged"/> (levantado na thread de captura do NAudio) para a
    /// thread da UI — research.md §4 / R2. Quando null, cai para <see cref="ImmediateUiDispatcher"/>,
    /// mantendo o comportamento anterior em cenários sem UI (testes, diagnóstico).
    /// </param>
    public AudioEngine(IUiDispatcher? uiDispatcher = null) =>
        _uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;

    public void Start(string? inputDeviceId, string outputDeviceId)
    {
        DiagLog.Write($"Start: chamado (uptime do processo = {Environment.TickCount64}ms)");
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var inputDevice = ResolveInputDevice(enumerator, inputDeviceId);
            DiagLog.Write($"Start: dispositivo de entrada resolvido, MixFormat atual = {DescribeFormat(inputDevice.AudioClient.MixFormat)}");

            var outputDevice = ResolveOutputDevice(enumerator, outputDeviceId);

            _capture = CreateAndStartCapture(inputDevice);
            DiagLog.Write($"Start: captura iniciada com sucesso, formato final = {DescribeFormat(_capture.WaveFormat)}");

            _outputBuffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
            };

            _output = CreateAndInitOutputWithRetry(outputDevice, _outputBuffer);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();

            var format = _capture.WaveFormat;
            _activeInputChannelCount = format.Channels;
            _routingMode = RoutingModeApplier.ResolveFallback(_routingMode, _activeInputChannelCount);

            var channelWarning = format.Channels != 2
                ? " ⚠ esperado 2 canais (Input1/Input2 seriam duplicados ou mal mapeados)"
                : string.Empty;
            _captureFormatDescription =
                $"{format.Channels}ch, {format.BitsPerSample}-bit {format.Encoding}, {format.SampleRate}Hz{channelWarning}";

            // Guarda os ids para a recuperação automática limitada (Stop+Start) do watchdog.
            _activeInputDeviceId = inputDeviceId;
            _activeOutputDeviceId = outputDeviceId;

            // Semeia "último dado recebido" com o instante do Start: sem isso o primeiro tick do
            // watchdog veria "nenhum dado desde sempre" e marcaria Stalled antes do primeiro buffer.
            var now = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref _lastDataTicks, now.UtcTicks);
            if (_health.MarkDataReceived(now))
            {
                RaiseHealthChanged();
            }

            StartWatchdog();
        }
        catch (Exception ex)
        {
            DiagLog.Write($"Start: FALHOU — {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
            // Não deixa a engine em estado parcial (ex.: captura iniciada mas saída não) se
            // qualquer etapa falhar no meio do caminho.
            Stop();
            throw;
        }
    }

    private static string DescribeFormat(WaveFormat format) =>
        $"{format.Channels}ch/{format.BitsPerSample}-bit/{format.SampleRate}Hz/{format.Encoding}" +
        (format is WaveFormatExtensible ext ? $"/sub={ext.SubFormat}" : string.Empty);

    /// <summary>
    /// Resolve o dispositivo de entrada pelo id informado, se ainda ativo; caso contrário (ou se
    /// nenhum id foi informado), cai para a mesma auto-detecção do AIR 192|4 usada antes desta
    /// extensão (research.md §4).
    /// </summary>
    private static MMDevice ResolveInputDevice(MMDeviceEnumerator enumerator, string? inputDeviceId)
    {
        if (inputDeviceId is not null)
        {
            try
            {
                var device = enumerator.GetDevice(inputDeviceId);
                if (device.State == DeviceState.Active)
                {
                    return device;
                }
            }
            catch (COMException)
            {
                // Dispositivo salvo não existe mais; cai para a auto-detecção abaixo.
            }
        }

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(AirDeviceNameFragment, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("AIR 192|4 não encontrado entre os dispositivos de captura ativos.");
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

    /// <summary>Tentativas de reabrir a captura quando o Windows ainda não liberou a sessão de
    /// áudio da execução anterior (achado na validação manual de campo/V1 — 18/20 aberturas
    /// consecutivas do app falhavam com AUDCLNT_E_UNSUPPORTED_FORMAT no AIR 192|4; ver research.md
    /// §1 S7). Bounded: nunca dispara indefinidamente.</summary>
    private const int MaxUnsupportedFormatRetries = 3;

    private static readonly TimeSpan UnsupportedFormatRetryDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Algumas interfaces reportam o "mix format" compartilhado como uma struct
    /// WAVEFORMATEXTENSIBLE que o próprio driver rejeita ao inicializar o AudioClient
    /// (AUDCLNT_E_UNSUPPORTED_FORMAT), mesmo aceitando o formato IEEE float "plano" equivalente
    /// (mesmos canais/sample rate). Se a tentativa padrão falhar com esse erro, tenta de novo
    /// forçando um WaveFormat IEEE float simples com os mesmos canais/sample rate.
    ///
    /// Se AMBAS as variações de formato falharem com o mesmo erro, a causa provável não é o
    /// formato em si, mas o driver do AIR 192|4 ainda não ter liberado a sessão de áudio da
    /// instância anterior do app (research.md §1 S7) — nesse caso, espera um pouco e repete o par
    /// de tentativas, até <see cref="MaxUnsupportedFormatRetries"/> vezes, antes de desistir.
    /// </summary>
    private WasapiCapture CreateAndStartCapture(MMDevice inputDevice)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = TryStartCapture(inputDevice, attempt);
                DiagLog.Write($"CreateAndStartCapture: sucesso na tentativa {attempt}");
                return result;
            }
            catch (COMException ex) when (IsUnsupportedFormat(ex) && attempt < MaxUnsupportedFormatRetries)
            {
                DiagLog.Write($"CreateAndStartCapture: tentativa {attempt} falhou (0x{ex.HResult:X8}), aguardando {UnsupportedFormatRetryDelay.TotalMilliseconds}ms antes de repetir");
                Thread.Sleep(UnsupportedFormatRetryDelay);
            }
        }
    }

    private WasapiCapture TryStartCapture(MMDevice inputDevice, int attempt)
    {
        var capture = new WasapiCapture(inputDevice);
        DiagLog.Write($"TryStartCapture[{attempt}]: tentando formato padrão {DescribeFormat(capture.WaveFormat)}");
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        try
        {
            capture.StartRecording();
            return capture;
        }
        catch (COMException ex) when (IsUnsupportedFormat(ex))
        {
            var fallbackFormat = WaveFormat.CreateIeeeFloatWaveFormat(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
            DiagLog.Write($"TryStartCapture[{attempt}]: formato padrão falhou (0x{ex.HResult:X8}), tentando fallback {DescribeFormat(fallbackFormat)}");
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.Dispose();

            capture = new WasapiCapture(inputDevice) { WaveFormat = fallbackFormat };
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            try
            {
                capture.StartRecording();
                return capture;
            }
            catch (Exception fallbackEx)
            {
                DiagLog.Write($"TryStartCapture[{attempt}]: fallback TAMBÉM falhou — {fallbackEx.GetType().Name} 0x{fallbackEx.HResult:X8}");
                // Achado ao investigar S7: se a tentativa de fallback também falhar, o AudioClient
                // desta segunda captura NUNCA era liberado (sem Dispose aqui) — ficava vivo,
                // segurando o dispositivo, e sabotava toda tentativa seguinte (inclusive as 3
                // repetições do retry acima). Provável causa real dos 18/20 falhando sempre, não
                // apenas o driver "não ter liberado a sessão anterior".
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
                throw;
            }
        }
    }

    /// <summary>Repetições de <see cref="CreateAndInitOutput"/> — achado ao investigar S7 com
    /// diagnóstico ao vivo (research.md §1): quando entrada e saída são o MESMO dispositivo físico
    /// (comum no AIR 192|4, usado para monitorar pelo próprio hardware), a captura sempre negocia
    /// com sucesso, mas a saída — negociada milissegundos depois, no mesmo barramento USB — falha
    /// na maioria das aberturas (0x88890008/0x88890004). O driver aparentemente precisa de um
    /// instante entre as duas negociações no mesmo dispositivo. Repetição bounded (nunca laço
    /// infinito), não polling: só dispara em resposta à falha do Start() atual.</summary>
    private const int MaxOutputInitRetries = 4;

    private static readonly TimeSpan OutputInitRetryDelay = TimeSpan.FromMilliseconds(250);

    private WasapiOut CreateAndInitOutputWithRetry(MMDevice outputDevice, BufferedWaveProvider source)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = CreateAndInitOutput(outputDevice, source);
                DiagLog.Write($"CreateAndInitOutputWithRetry: sucesso na tentativa {attempt}");
                return result;
            }
            catch (COMException ex) when (IsUnsupportedFormatOrInvalidated(ex) && attempt < MaxOutputInitRetries)
            {
                DiagLog.Write($"CreateAndInitOutputWithRetry: tentativa {attempt} falhou (0x{ex.HResult:X8}), aguardando {OutputInitRetryDelay.TotalMilliseconds}ms antes de repetir");
                Thread.Sleep(OutputInitRetryDelay);
            }
        }
    }

    private static bool IsUnsupportedFormatOrInvalidated(COMException ex) =>
        unchecked((uint)ex.HResult) is 0x88890008 or 0x88890004;

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
        DiagLog.Write($"CreateAndInitOutput: source={DescribeFormat(source.WaveFormat)} outputMixFormat={DescribeFormat(outputDevice.AudioClient.MixFormat)}");
        var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        try
        {
            output.Init(source);
            DiagLog.Write("CreateAndInitOutput: sucesso na tentativa 1 (source direto)");
            return output;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"CreateAndInitOutput: tentativa 1 (source direto) falhou — {ex.GetType().Name} 0x{ex.HResult:X8}");
            output.Dispose();
            if (ex is not COMException comEx || !IsUnsupportedFormat(comEx))
            {
                throw;
            }
        }

        EnsureMediaFoundationStarted();
        var outputMixFormat = outputDevice.AudioClient.MixFormat;

        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        try
        {
            _resampler = new MediaFoundationResampler(source, outputMixFormat) { ResamplerQuality = 60 };
            output.Init(_resampler);
            DiagLog.Write($"CreateAndInitOutput: sucesso na tentativa 2 (resample p/ {DescribeFormat(outputMixFormat)})");
            return output;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"CreateAndInitOutput: tentativa 2 (resample p/ {DescribeFormat(outputMixFormat)}) falhou — {ex.GetType().Name} 0x{ex.HResult:X8}");
            output.Dispose();
            _resampler?.Dispose();
            if (ex is not COMException comEx || !IsUnsupportedFormat(comEx))
            {
                throw;
            }
        }

        var plainFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputMixFormat.SampleRate, outputMixFormat.Channels);
        output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 50);
        _resampler = new MediaFoundationResampler(source, plainFormat) { ResamplerQuality = 60 };
        try
        {
            output.Init(_resampler);
            DiagLog.Write($"CreateAndInitOutput: sucesso na tentativa 3 (resample p/ {DescribeFormat(plainFormat)})");
            return output;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"CreateAndInitOutput: tentativa 3 (resample p/ {DescribeFormat(plainFormat)}) TAMBÉM falhou — {ex.GetType().Name} 0x{ex.HResult:X8}");
            // Mesma classe de vazamento corrigida em TryStartCapture (S7): sem isto, um AudioClient
            // de saída jamais liberado sobrevive à exceção e segura o dispositivo de saída.
            output.Dispose();
            _resampler?.Dispose();
            throw;
        }
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
        StopWatchdog();

        // Desassina ANTES de parar: RecordingStopped/PlaybackStopped também disparam em uma parada
        // deliberada (Stop do usuário, pausa de reconfiguração, troca de dispositivo). Desassinar é
        // mais determinístico do que um flag "parada intencional" — evita que uma parada esperada
        // seja contabilizada como congelamento (falso Stalled).
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
        }

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
        }

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
        _activeInputChannelCount = 0;
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

    public RoutingMode RoutingMode => _routingMode;

    public void SetRoutingMode(RoutingMode mode) => _routingMode = RoutingModeApplier.ResolveFallback(mode, _activeInputChannelCount);

    public int ActiveInputChannelCount => _activeInputChannelCount;

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

        // Regra 1 do contrato de saúde do fluxo: cada buffer atualiza "último dado recebido". Só um
        // Interlocked aqui (nada de lock na thread de captura); a transição de estado em si é feita
        // pelo watchdog, na thread da UI.
        Interlocked.Exchange(ref _lastDataTicks, DateTimeOffset.UtcNow.UtcTicks);

        var format = _capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var channelCount = format.Channels;
        var sampleCount = e.BytesRecorded / bytesPerSample / channelCount;

        var input1Samples = new float[sampleCount];
        var input2Samples = new float[sampleCount];
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

            var input1 = leftRaw * leftGain;
            var input2 = rightRaw * rightGain;
            input1Samples[i] = input1;
            input2Samples[i] = input2;

            var input1Out = leftAudible && _monitoringEnabled ? input1 : 0f;
            var input2Out = rightAudible && _monitoringEnabled ? input2 : 0f;

            // Roteamento aplicado depois de trim/mute/solo já resolvidos (FR-006), alimentando
            // apenas o buffer de saída (caminho audível). Os meters usam o par pré-gate/
            // pré-roteamento (input1Samples/input2Samples) para nunca silenciar com mute/solo/
            // monitoramento desativado (research.md §1).
            var (left, right) = RoutingModeApplier.Apply(_routingMode, input1Out, input2Out);

            SampleFormatIO.WriteSample(processed, frameOffset, left, format);
            if (channelCount > 1)
            {
                SampleFormatIO.WriteSample(processed, frameOffset + bytesPerSample, right, format);
            }
        }

        _outputBuffer.AddSamples(processed, 0, processed.Length);

        RaiseLevels(InputChannelId.Input1, input1Samples);
        RaiseLevels(InputChannelId.Input2, input2Samples);
    }

    /// <summary>
    /// Calcula peak/RMS (mesma fonte de dados pré-gate/pré-roteamento da feature 003 — FR-010/FR-021,
    /// intocada) e entrega <see cref="LevelsChanged"/> SEMPRE na thread da UI. Como
    /// <c>DataAvailable</c> dispara dezenas de vezes por segundo, os níveis são **coalescidos**: só
    /// há um despacho pendente por vez e ele carrega o último valor de cada canal, em vez de
    /// inundar a fila do dispatcher (research.md §4 / R2).
    /// </summary>
    private void RaiseLevels(InputChannelId channel, ReadOnlySpan<float> samples)
    {
        var peakDb = LevelMetering.CalculatePeakDb(samples);
        var rmsDb = LevelMetering.CalculateRmsDb(samples);
        var isClipping = LevelMetering.IsClipping(peakDb);
        var args = new ChannelLevelsChangedEventArgs(channel, peakDb, rmsDb, isClipping);

        if (_uiDispatcher.IsOnUiThread)
        {
            LevelsChanged?.Invoke(this, args);
            return;
        }

        bool scheduleDispatch;
        lock (_pendingLevelsLock)
        {
            _pendingLevels[channel] = args;
            scheduleDispatch = !_levelsDispatchScheduled;
            _levelsDispatchScheduled = true;
        }

        if (scheduleDispatch)
        {
            _uiDispatcher.Post(FlushPendingLevels);
        }
    }

    // --- Saúde do fluxo: watchdog + eventos de parada do NAudio (contrato audio-stream-health) ---

    /// <summary>
    /// Watchdog de saúde do fluxo. Roda em um <see cref="System.Threading.Timer"/> mas TODO o
    /// trabalho é despachado para a thread da UI via <see cref="IUiDispatcher"/> — equivalente ao
    /// <c>DispatcherTimer</c> pedido pelo contrato, sem trazer WPF para <c>AirControl.Audio</c>
    /// (Constitution I). MUST NOT consultar o driver: só compara <c>agora - último dado recebido</c>
    /// (FR-015b/SC-004b).
    /// </summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = new System.Threading.Timer(
            _ => _uiDispatcher.Post(OnWatchdogTick),
            null,
            WatchdogInterval,
            WatchdogInterval);
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    /// <summary>
    /// Único ponto que muta <see cref="_health"/> depois do Start — sempre na thread da UI. O
    /// caminho de captura só escreve <see cref="_lastDataTicks"/> (interlocked), o que evita
    /// qualquer lock compartilhado entre a thread de captura e a thread da UI (um lock aqui poderia
    /// travar <c>StopRecording</c>, que espera a thread de captura sair).
    /// </summary>
    private void OnWatchdogTick()
    {
        var lastData = new DateTimeOffset(Interlocked.Read(ref _lastDataTicks), TimeSpan.Zero);
        var changed = false;

        if (_health.LastDataReceivedAt is null || lastData > _health.LastDataReceivedAt.Value)
        {
            changed |= _health.MarkDataReceived(lastData);
        }

        changed |= _health.EvaluateStaleness(DateTimeOffset.UtcNow);

        if (changed)
        {
            RaiseHealthChanged();
        }

        if (_health.State == AudioStreamState.Stalled)
        {
            TryRecover();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) =>
        SignalStreamStopped("WasapiCapture.RecordingStopped", e.Exception);

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) =>
        SignalStreamStopped("WasapiOut.PlaybackStopped", e.Exception);

    /// <summary>
    /// Regra 3 do contrato: uma parada sinalizada pelo NAudio (com ou sem exceção) NUNCA é engolida
    /// — vira <see cref="AudioStreamState.Stalled"/> e dispara a recuperação limitada. Marshalado
    /// para a thread da UI porque esses eventos chegam na thread de captura/reprodução.
    /// </summary>
    private void SignalStreamStopped(string source, Exception? exception)
    {
        var reason = exception is null ? source : $"{source}: {exception.Message}";

        _uiDispatcher.Post(() =>
        {
            if (_health.MarkStalled(DateTimeOffset.UtcNow, reason))
            {
                RaiseHealthChanged();
            }

            TryRecover();
        });
    }

    /// <summary>
    /// Recuperação automática LIMITADA (≤ 2 tentativas, backoff curto) via a política pura de
    /// <c>AirControl.Core</c>; esgotada, cai para <see cref="AudioStreamState.Faulted"/> com
    /// mensagem acionável — nunca um laço de reinício infinito (FR-007, regra 4 do contrato).
    /// </summary>
    private void TryRecover()
    {
        if (_isRecovering || _health.State != AudioStreamState.Stalled)
        {
            return;
        }

        _isRecovering = true;
        try
        {
            AudioStreamRecoveryPolicy.Recover(
                _health,
                RestartForRecovery,
                () => DateTimeOffset.UtcNow,
                Thread.Sleep);
        }
        finally
        {
            _isRecovering = false;
        }

        RaiseHealthChanged();
    }

    private void RestartForRecovery()
    {
        if (_activeOutputDeviceId is null)
        {
            throw new InvalidOperationException("nenhum dispositivo de saída ativo para restabelecer o fluxo");
        }

        Start(_activeInputDeviceId, _activeOutputDeviceId);
    }

    private void RaiseHealthChanged()
    {
        var args = new AudioStreamHealthChangedEventArgs(_health.State, _health.FaultReason, _health.RecoveryAttempts);

        if (_uiDispatcher.IsOnUiThread)
        {
            StreamHealthChanged?.Invoke(this, args);
            return;
        }

        _uiDispatcher.Post(() => StreamHealthChanged?.Invoke(this, args));
    }

    private void FlushPendingLevels()
    {
        ChannelLevelsChangedEventArgs?[] batch;
        lock (_pendingLevelsLock)
        {
            batch = Channels
                .Select(channel => _pendingLevels.TryGetValue(channel, out var args) ? args : null)
                .ToArray();
            _pendingLevels.Clear();
            _levelsDispatchScheduled = false;
        }

        foreach (var args in batch)
        {
            if (args is not null)
            {
                LevelsChanged?.Invoke(this, args);
            }
        }
    }

    public void Dispose()
    {
        StopWatchdog();
        Stop();
    }
}
