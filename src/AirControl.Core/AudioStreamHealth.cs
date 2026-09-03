namespace AirControl.Core;

public enum AudioStreamState
{
    /// <summary>Dados chegando; os medidores refletem o sinal.</summary>
    Delivering,

    /// <summary>Sem dados além do limiar OU evento de parada do NAudio — recuperação limitada em curso.</summary>
    Stalled,

    /// <summary>Recuperação automática esgotada; <see cref="AudioStreamHealth.FaultReason"/> é acionável.</summary>
    Faulted,
}

/// <summary>
/// Estado de saúde do fluxo de captura/reprodução — a fonte de verdade para "os medidores estão
/// vivos?" (data-model.md §1, contracts/audio-stream-health-contract.md).
/// </summary>
/// <remarks>
/// Corrige S2 (research.md §1): sem watchdog e sem assinatura de
/// <c>RecordingStopped</c>/<c>PlaybackStopped</c>, uma parada externa do fluxo (suspensão, perda
/// para modo exclusivo, driver reiniciado, dispositivo removido) deixava os medidores congelados no
/// último valor e nada reagia. Toda a política é pura e testável sem timer nem hardware: a
/// avaliação de staleness recebe "agora" e o último timestamp de dados.
/// </remarks>
public sealed class AudioStreamHealth
{
    /// <summary>5s de silêncio de dados — casa com SC-002 (nunca congelado &gt; 5s sem estado de erro).</summary>
    public static readonly TimeSpan DefaultStalenessThreshold = TimeSpan.FromSeconds(5);

    /// <summary>2 tentativas antes de <see cref="AudioStreamState.Faulted"/> — nunca laço infinito (FR-007).</summary>
    public const int DefaultMaxRecoveryAttempts = 2;

    public AudioStreamHealth(TimeSpan? stalenessThreshold = null, int maxRecoveryAttempts = DefaultMaxRecoveryAttempts)
    {
        StalenessThreshold = stalenessThreshold ?? DefaultStalenessThreshold;
        MaxRecoveryAttempts = maxRecoveryAttempts;
    }

    public TimeSpan StalenessThreshold { get; }

    public int MaxRecoveryAttempts { get; }

    public AudioStreamState State { get; private set; } = AudioStreamState.Delivering;

    public DateTimeOffset? LastDataReceivedAt { get; private set; }

    public int RecoveryAttempts { get; private set; }

    public string? FaultReason { get; private set; }

    /// <summary>Motivo diagnóstico da última parada (ex.: "RecordingStopped: dispositivo removido").</summary>
    public string? LastStallReason { get; private set; }

    /// <summary>Instante em que o fluxo foi marcado como parado; null enquanto entregando.</summary>
    public DateTimeOffset? StalledAt { get; private set; }

    /// <summary>
    /// Avaliação de staleness pura (regra 6 do contrato): sem relógio interno, sem timer, sem
    /// consultar o driver — só compara "agora" com o último dado recebido.
    /// </summary>
    public static bool IsStale(DateTimeOffset now, DateTimeOffset? lastDataReceivedAt, TimeSpan threshold) =>
        lastDataReceivedAt is null || now - lastDataReceivedAt.Value >= threshold;

    /// <summary>
    /// Regra 1 do contrato: chegou dado ⇒ <see cref="AudioStreamState.Delivering"/>, tentativas
    /// zeradas e erro limpo. Retorna true quando o estado observável mudou.
    /// </summary>
    public bool MarkDataReceived(DateTimeOffset now)
    {
        LastDataReceivedAt = now;

        if (State == AudioStreamState.Delivering)
        {
            return false;
        }

        State = AudioStreamState.Delivering;
        RecoveryAttempts = 0;
        FaultReason = null;
        LastStallReason = null;
        StalledAt = null;
        return true;
    }

    /// <summary>
    /// Regra 3 do contrato: um evento de parada do NAudio (com ou sem exceção) leva a
    /// <see cref="AudioStreamState.Stalled"/> — nunca é engolido. Retorna true quando o estado mudou.
    /// </summary>
    public bool MarkStalled(DateTimeOffset now, string? reason = null)
    {
        if (State != AudioStreamState.Delivering)
        {
            return false;
        }

        State = AudioStreamState.Stalled;
        FaultReason = null;
        // O motivo da parada é guardado como diagnóstico; ele só vira mensagem acionável ao
        // usuário quando a recuperação automática esgota (Faulted).
        LastStallReason = reason;
        StalledAt = now;
        return true;
    }

    /// <summary>Tick do watchdog: sinaliza <see cref="AudioStreamState.Stalled"/> se o silêncio passou do limiar.</summary>
    public bool EvaluateStaleness(DateTimeOffset now) =>
        State == AudioStreamState.Delivering
        && IsStale(now, LastDataReceivedAt, StalenessThreshold)
        && MarkStalled(now);

    /// <summary>
    /// Regra 4 do contrato: registra mais uma tentativa de recuperação. Retorna false — e transita
    /// para <see cref="AudioStreamState.Faulted"/> com <paramref name="exhaustedReason"/> — quando o
    /// teto de <see cref="MaxRecoveryAttempts"/> já foi atingido. É isso que impede o laço infinito.
    /// </summary>
    public bool TryRegisterRecoveryAttempt(string exhaustedReason)
    {
        if (State == AudioStreamState.Faulted)
        {
            return false;
        }

        if (RecoveryAttempts >= MaxRecoveryAttempts)
        {
            MarkFaulted(exhaustedReason);
            return false;
        }

        RecoveryAttempts++;
        return true;
    }

    /// <summary>Entra em <see cref="AudioStreamState.Faulted"/> com um motivo acionável (nunca vazio).</summary>
    public bool MarkFaulted(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Faulted exige um motivo acionável (Constitution III / FR-003).", nameof(reason));
        }

        var changed = State != AudioStreamState.Faulted;
        State = AudioStreamState.Faulted;
        FaultReason = reason;
        return changed;
    }
}

/// <summary>
/// Política pura de recuperação automática limitada (research.md §2). Compartilhada pelo
/// <c>AudioEngine</c> real e pelos fakes de teste para que a regra de "no máximo 2 tentativas e
/// depois um erro acionável" exista em um lugar só.
/// </summary>
public static class AudioStreamRecoveryPolicy
{
    /// <summary>Backoff curto entre tentativas — cabe no orçamento de 3s de recuperação (SC-003).</summary>
    public static readonly TimeSpan DefaultBackoff = TimeSpan.FromMilliseconds(200);

    private const string BaseFaultMessage =
        "O fluxo de áudio parou e não foi possível restabelecê-lo automaticamente";

    private const string ActionSuffix =
        "Reconecte o AIR 192|4 ou use \"Reiniciar conexão\".";

    public static void Recover(
        AudioStreamHealth health,
        Action restartCapture,
        Func<DateTimeOffset> clock,
        Action<TimeSpan>? wait = null,
        TimeSpan? backoff = null)
    {
        var delay = backoff ?? DefaultBackoff;
        Exception? lastError = null;

        while (health.State == AudioStreamState.Stalled)
        {
            var exhaustedReason = lastError is null
                ? $"{BaseFaultMessage}. {ActionSuffix}"
                : $"{BaseFaultMessage}: {lastError.Message}. {ActionSuffix}";

            if (!health.TryRegisterRecoveryAttempt(exhaustedReason))
            {
                return;
            }

            try
            {
                restartCapture();
                health.MarkDataReceived(clock());
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (delay > TimeSpan.Zero)
                {
                    wait?.Invoke(delay);
                }
            }
        }
    }
}
