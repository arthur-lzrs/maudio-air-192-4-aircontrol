namespace AirControl.Core;

/// <summary>
/// Gatilhos permitidos de uma pausa de reconfiguração (FR-015b). <b>Nenhum outro.</b> Não existe
/// caminho periódico/especulativo que dispare uma pausa — SC-004b: 30 min sem tocar em formato/driver
/// significa zero pausas.
/// </summary>
public enum ReconfigurationTrigger
{
    OpenFormatList,
    ChangeDriverSampleRate,
    ChangeActiveDevice,
    Startup,
}

public enum ReconfigurationPhase
{
    /// <summary>Pausa em curso — MUST ser visível ao usuário ("Reconfigurando…", FR-015c).</summary>
    InProgress,

    /// <summary>Captura restabelecida dentro do teto.</summary>
    Completed,

    /// <summary>Teto excedido, mutação falhou, ou o Start não restabeleceu — estado acionável (FR-015d).</summary>
    Faulted,
}

public sealed record ReconfigurationResult(ReconfigurationPhase Phase, string? FaultReason, TimeSpan Elapsed)
{
    public bool IsCompleted => Phase == ReconfigurationPhase.Completed;
}

public sealed record ReconfigurationPauseChangedEventArgs(
    ReconfigurationTrigger Trigger,
    ReconfigurationPhase Phase,
    string? FaultReason);

/// <summary>
/// Unifica os três pontos que hoje fazem <c>Stop → mutar dispositivo → Start</c> de forma ad-hoc
/// (<c>RecordingFormatSelectorViewModel.OnSelectedFormatChanged</c>,
/// <c>DriverSettingsViewModel.OnSelectedSampleRateChanged</c>, e a consulta ASIO para filtrar
/// formatos) em uma operação única, deliberada, limitada e sinalizada
/// (contracts/reconfiguration-pause-contract.md).
/// </summary>
/// <remarks>
/// Corrige S5 (research.md §1): o <c>Start</c> de restauração ficava FORA de um <c>finally</c>, então
/// uma mutação que lançasse deixava a engine parada e a exceção subia até o
/// <c>DispatcherUnhandledException</c> — captura morta, sem nenhum estado acionável. Aqui o
/// restabelecimento acontece em <b>todos</b> os caminhos.
/// Corrige S3: a consulta em tempo real do sample rate do driver passa a acontecer DENTRO da pausa
/// (captura parada), nunca com a captura ativa.
/// </remarks>
public sealed class ReconfigurationPause
{
    /// <summary>Teto de duração da pausa: 2s (SC-004a).</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(2);

    private readonly Action _stopCapture;
    private readonly Action _startCapture;
    private readonly Func<DateTimeOffset> _clock;

    public ReconfigurationPause(Action stopCapture, Action startCapture, Func<DateTimeOffset>? clock = null)
    {
        _stopCapture = stopCapture ?? throw new ArgumentNullException(nameof(stopCapture));
        _startCapture = startCapture ?? throw new ArgumentNullException(nameof(startCapture));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Fase da última pausa (null antes da primeira). Observável pela UI durante toda a operação.</summary>
    public ReconfigurationPhase? Phase { get; private set; }

    /// <summary>Quantas pausas já foram executadas — base da verificação de SC-004b (zero pausas sem ação).</summary>
    public int PauseCount { get; private set; }

    public event EventHandler<ReconfigurationPauseChangedEventArgs>? PhaseChanged;

    /// <summary>
    /// Executa <paramref name="mutateDevice"/> dentro de uma janela controlada:
    /// <c>Stop captura → (mutação) → Start captura</c>, sempre restabelecendo no <c>finally</c>.
    /// </summary>
    public ReconfigurationResult RunPause(
        ReconfigurationTrigger trigger,
        Action mutateDevice,
        TimeSpan? deadline = null)
    {
        if (!Enum.IsDefined(trigger))
        {
            // Regra FR-015b: só os quatro gatilhos discretos existem. Um valor fora do enum
            // significa um caminho novo (provavelmente periódico) tentando pausar a captura.
            throw new ArgumentOutOfRangeException(
                nameof(trigger),
                trigger,
                "Uma pausa de reconfiguração só pode ser disparada por um gatilho discreto conhecido (FR-015b).");
        }

        ArgumentNullException.ThrowIfNull(mutateDevice);

        var effectiveDeadline = deadline ?? DefaultDeadline;
        var startedAt = _clock();
        PauseCount++;
        SetPhase(trigger, ReconfigurationPhase.InProgress, null);

        string? faultReason = null;

        try
        {
            _stopCapture();

            try
            {
                mutateDevice();
            }
            catch (Exception ex)
            {
                faultReason = $"Falha ao reconfigurar o dispositivo: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            faultReason = $"Falha ao pausar a captura para reconfigurar: {ex.Message}";
        }
        finally
        {
            // Regra 2 do contrato: a captura é restabelecida em TODOS os caminhos, inclusive quando
            // a mutação lança. É exatamente o que faltava em S5.
            try
            {
                _startCapture();
            }
            catch (Exception ex)
            {
                faultReason = $"Falha ao restabelecer a captura após a reconfiguração: {ex.Message}";
            }
        }

        var elapsed = _clock() - startedAt;

        if (faultReason is null && elapsed > effectiveDeadline)
        {
            faultReason =
                $"A reconfiguração excedeu o limite de {effectiveDeadline.TotalSeconds:0.#}s "
                + $"({elapsed.TotalSeconds:0.#}s). Verifique o dispositivo e tente novamente.";
        }

        var result = faultReason is null
            ? new ReconfigurationResult(ReconfigurationPhase.Completed, null, elapsed)
            : new ReconfigurationResult(ReconfigurationPhase.Faulted, faultReason, elapsed);

        SetPhase(trigger, result.Phase, result.FaultReason);
        return result;
    }

    private void SetPhase(ReconfigurationTrigger trigger, ReconfigurationPhase phase, string? faultReason)
    {
        Phase = phase;
        PhaseChanged?.Invoke(this, new ReconfigurationPauseChangedEventArgs(trigger, phase, faultReason));
    }
}
