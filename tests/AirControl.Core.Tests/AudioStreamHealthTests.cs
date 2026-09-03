using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

/// <summary>
/// Regressão de S2 (research.md §1) na camada pura: sem watchdog e sem estado de saúde, uma parada
/// externa do fluxo (suspensão, perda para modo exclusivo, driver reiniciado) deixava os medidores
/// congelados no último valor e nada reagia. Cobre as regras 1/4/6 de
/// contracts/audio-stream-health-contract.md.
/// </summary>
public class AudioStreamHealthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewHealth_StartsDelivering()
    {
        var health = new AudioStreamHealth();

        Assert.Equal(AudioStreamState.Delivering, health.State);
        Assert.Null(health.FaultReason);
        Assert.Equal(0, health.RecoveryAttempts);
    }

    [Fact]
    public void DefaultPolicy_MatchesResearchDefaults()
    {
        var health = new AudioStreamHealth();

        Assert.Equal(TimeSpan.FromSeconds(5), health.StalenessThreshold);
        Assert.Equal(2, health.MaxRecoveryAttempts);
    }

    // --- staleness pura (regra 6 do contrato) -------------------------------------------------

    [Fact]
    public void IsStale_BeforeThreshold_IsFalse()
    {
        Assert.False(AudioStreamHealth.IsStale(T0.AddSeconds(4), T0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void IsStale_AtOrAfterThreshold_IsTrue()
    {
        Assert.True(AudioStreamHealth.IsStale(T0.AddSeconds(5), T0, TimeSpan.FromSeconds(5)));
        Assert.True(AudioStreamHealth.IsStale(T0.AddSeconds(9), T0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void IsStale_WithNoDataEverReceived_IsTrue()
    {
        Assert.True(AudioStreamHealth.IsStale(T0, lastDataReceivedAt: null, TimeSpan.FromSeconds(5)));
    }

    // --- transições ---------------------------------------------------------------------------

    [Fact]
    public void EvaluateStaleness_WithSilenceBeyondThreshold_TransitionsToStalled()
    {
        var health = new AudioStreamHealth();
        health.MarkDataReceived(T0);

        Assert.False(health.EvaluateStaleness(T0.AddSeconds(4)));
        Assert.Equal(AudioStreamState.Delivering, health.State);

        Assert.True(health.EvaluateStaleness(T0.AddSeconds(5)));
        Assert.Equal(AudioStreamState.Stalled, health.State);
    }

    /// <summary>Regra 1: dados voltando restauram Delivering e zeram as tentativas.</summary>
    [Fact]
    public void MarkDataReceived_AfterStall_ReturnsToDeliveringAndResetsAttempts()
    {
        var health = new AudioStreamHealth();
        health.MarkDataReceived(T0);
        health.EvaluateStaleness(T0.AddSeconds(6));
        health.TryRegisterRecoveryAttempt("esgotado");
        Assert.Equal(1, health.RecoveryAttempts);

        Assert.True(health.MarkDataReceived(T0.AddSeconds(7)));

        Assert.Equal(AudioStreamState.Delivering, health.State);
        Assert.Equal(0, health.RecoveryAttempts);
        Assert.Null(health.FaultReason);
    }

    /// <summary>Regra 3: um evento de parada do NAudio com exceção leva a Stalled (não é engolido).</summary>
    [Fact]
    public void MarkStalled_FromStopEvent_TransitionsAndKeepsReason()
    {
        var health = new AudioStreamHealth();
        health.MarkDataReceived(T0);

        Assert.True(health.MarkStalled(T0.AddSeconds(1), "RecordingStopped: dispositivo removido"));

        Assert.Equal(AudioStreamState.Stalled, health.State);
    }

    [Fact]
    public void MarkStalled_WhenAlreadyStalled_DoesNotReportAnotherChange()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "primeiro");

        Assert.False(health.MarkStalled(T0.AddSeconds(1), "segundo"));
    }

    // --- teto de recuperação (regra 4: nunca laço infinito) -------------------------------------

    [Fact]
    public void TryRegisterRecoveryAttempt_AllowsExactlyTwoAttemptsThenFaults()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "parou");

        Assert.True(health.TryRegisterRecoveryAttempt("esgotado"));
        Assert.True(health.TryRegisterRecoveryAttempt("esgotado"));

        Assert.False(health.TryRegisterRecoveryAttempt("esgotado"));
        Assert.Equal(AudioStreamState.Faulted, health.State);
        Assert.Equal(2, health.RecoveryAttempts);
        Assert.Equal("esgotado", health.FaultReason);
    }

    /// <summary>Contra-exemplo do contrato: Faulted sem FaultReason acionável.</summary>
    [Fact]
    public void Faulted_AlwaysCarriesAnActionableReason()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "parou");
        health.TryRegisterRecoveryAttempt("r");
        health.TryRegisterRecoveryAttempt("r");
        health.TryRegisterRecoveryAttempt("Não foi possível restabelecer o fluxo. Reconecte o dispositivo.");

        Assert.Equal(AudioStreamState.Faulted, health.State);
        Assert.False(string.IsNullOrWhiteSpace(health.FaultReason));
    }

    [Fact]
    public void FullCycle_DeliveringStalledFaultedDelivering()
    {
        var health = new AudioStreamHealth();
        health.MarkDataReceived(T0);
        Assert.Equal(AudioStreamState.Delivering, health.State);

        health.EvaluateStaleness(T0.AddSeconds(6));
        Assert.Equal(AudioStreamState.Stalled, health.State);

        health.TryRegisterRecoveryAttempt("esgotado");
        health.TryRegisterRecoveryAttempt("esgotado");
        health.TryRegisterRecoveryAttempt("esgotado");
        Assert.Equal(AudioStreamState.Faulted, health.State);

        // Ação do usuário (reconectar/trocar dispositivo) → os dados voltam.
        Assert.True(health.MarkDataReceived(T0.AddSeconds(20)));
        Assert.Equal(AudioStreamState.Delivering, health.State);
        Assert.Equal(0, health.RecoveryAttempts);
    }

    [Fact]
    public void EvaluateStaleness_WhenAlreadyFaulted_DoesNotChangeState()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "parou");
        health.TryRegisterRecoveryAttempt("r");
        health.TryRegisterRecoveryAttempt("r");
        health.TryRegisterRecoveryAttempt("r");

        Assert.False(health.EvaluateStaleness(T0.AddMinutes(5)));
        Assert.Equal(AudioStreamState.Faulted, health.State);
    }

    // --- política de recuperação limitada -------------------------------------------------------

    [Fact]
    public void RecoveryPolicy_WithSuccessfulRestart_ReturnsToDelivering()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "parou");
        var restarts = 0;

        AudioStreamRecoveryPolicy.Recover(
            health,
            () => restarts++,
            () => T0.AddSeconds(1),
            backoff: TimeSpan.Zero);

        Assert.Equal(1, restarts);
        Assert.Equal(AudioStreamState.Delivering, health.State);
    }

    /// <summary>Nunca um laço infinito de reinício: no máximo 2 tentativas, depois Faulted (FR-007).</summary>
    [Fact]
    public void RecoveryPolicy_WithAlwaysFailingRestart_StopsAtTwoAttemptsAndFaults()
    {
        var health = new AudioStreamHealth();
        health.MarkStalled(T0, "parou");
        var restarts = 0;

        AudioStreamRecoveryPolicy.Recover(
            health,
            () => { restarts++; throw new InvalidOperationException("dispositivo indisponível"); },
            () => T0.AddSeconds(1),
            backoff: TimeSpan.Zero);

        Assert.Equal(2, restarts);
        Assert.Equal(AudioStreamState.Faulted, health.State);
        Assert.Contains("dispositivo indisponível", health.FaultReason);
    }

    [Fact]
    public void RecoveryPolicy_WhenNotStalled_DoesNothing()
    {
        var health = new AudioStreamHealth();
        health.MarkDataReceived(T0);
        var restarts = 0;

        AudioStreamRecoveryPolicy.Recover(health, () => restarts++, () => T0, backoff: TimeSpan.Zero);

        Assert.Equal(0, restarts);
        Assert.Equal(AudioStreamState.Delivering, health.State);
    }
}
