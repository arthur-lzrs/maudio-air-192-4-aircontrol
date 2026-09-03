using AirControl.Core;
using Xunit;

namespace AirControl.Core.Tests;

/// <summary>
/// Regressão de S5/S3 (research.md §1) na camada pura, contra
/// contracts/reconfiguration-pause-contract.md: restabelecimento garantido em todos os caminhos,
/// teto de duração, e só gatilhos discretos.
/// </summary>
public class ReconfigurationPauseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static (ReconfigurationPause Pause, List<string> Log) CreatePause(Func<DateTimeOffset>? clock = null)
    {
        var log = new List<string>();
        var pause = new ReconfigurationPause(
            () => log.Add("stop"),
            () => log.Add("start"),
            clock);
        return (pause, log);
    }

    [Fact]
    public void RunPause_HappyPath_StopsMutatesStartsAndCompletes()
    {
        var (pause, log) = CreatePause(() => T0);

        var result = pause.RunPause(ReconfigurationTrigger.OpenFormatList, () => log.Add("mutate"));

        Assert.True(result.IsCompleted);
        Assert.Null(result.FaultReason);
        Assert.Equal(new[] { "stop", "mutate", "start" }, log);
        Assert.Equal(ReconfigurationPhase.Completed, pause.Phase);
    }

    /// <summary>Regra 2 / bug S5: uma mutação que lança NÃO pode deixar a captura parada.</summary>
    [Fact]
    public void RunPause_WithThrowingMutation_StillReestablishesCaptureAndFaults()
    {
        var (pause, log) = CreatePause(() => T0);

        var result = pause.RunPause(
            ReconfigurationTrigger.ChangeDriverSampleRate,
            () => throw new InvalidOperationException("driver recusou"));

        Assert.Equal(new[] { "stop", "start" }, log);
        Assert.False(result.IsCompleted);
        Assert.Contains("driver recusou", result.FaultReason);
        Assert.Equal(ReconfigurationPhase.Faulted, pause.Phase);
    }

    /// <summary>A exceção da mutação nunca escapa para o DispatcherUnhandledException (bug S5).</summary>
    [Fact]
    public void RunPause_WithThrowingMutation_DoesNotPropagateTheException()
    {
        var (pause, _) = CreatePause(() => T0);

        var result = pause.RunPause(ReconfigurationTrigger.Startup, () => throw new InvalidOperationException("boom"));

        Assert.Equal(ReconfigurationPhase.Faulted, result.Phase);
    }

    /// <summary>Regra 3: teto excedido → Faulted com estado acionável, nunca pausa silenciosa.</summary>
    [Fact]
    public void RunPause_ExceedingDeadline_Faults()
    {
        var now = T0;
        var pause = new ReconfigurationPause(
            () => now = now.AddSeconds(3),
            () => { },
            () => now);

        var result = pause.RunPause(ReconfigurationTrigger.ChangeActiveDevice, () => { }, TimeSpan.FromSeconds(2));

        Assert.False(result.IsCompleted);
        Assert.False(string.IsNullOrWhiteSpace(result.FaultReason));
        Assert.True(result.Elapsed > TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RunPause_WithinDeadline_Completes()
    {
        var now = T0;
        var pause = new ReconfigurationPause(
            () => now = now.AddMilliseconds(400),
            () => now = now.AddMilliseconds(400),
            () => now);

        var result = pause.RunPause(ReconfigurationTrigger.OpenFormatList, () => { }, TimeSpan.FromSeconds(2));

        Assert.True(result.IsCompleted);
    }

    [Fact]
    public void DefaultDeadline_IsTwoSeconds() => Assert.Equal(TimeSpan.FromSeconds(2), ReconfigurationPause.DefaultDeadline);

    /// <summary>Regra 2: mesmo quando o Stop falha, o Start de restauração é tentado.</summary>
    [Fact]
    public void RunPause_WithThrowingStop_StillAttemptsToReestablishCapture()
    {
        var log = new List<string>();
        var pause = new ReconfigurationPause(
            () => throw new InvalidOperationException("stop falhou"),
            () => log.Add("start"),
            () => T0);

        var result = pause.RunPause(ReconfigurationTrigger.Startup, () => log.Add("mutate"));

        Assert.Equal(new[] { "start" }, log);
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void RunPause_WithThrowingStart_FaultsWithAnActionableReason()
    {
        var pause = new ReconfigurationPause(
            () => { },
            () => throw new InvalidOperationException("dispositivo sumiu"),
            () => T0);

        var result = pause.RunPause(ReconfigurationTrigger.ChangeActiveDevice, () => { });

        Assert.False(result.IsCompleted);
        Assert.Contains("dispositivo sumiu", result.FaultReason);
    }

    /// <summary>FR-015b: só os quatro gatilhos discretos são aceitos.</summary>
    [Theory]
    [InlineData(ReconfigurationTrigger.OpenFormatList)]
    [InlineData(ReconfigurationTrigger.ChangeDriverSampleRate)]
    [InlineData(ReconfigurationTrigger.ChangeActiveDevice)]
    [InlineData(ReconfigurationTrigger.Startup)]
    public void RunPause_AcceptsEveryDiscreteTrigger(ReconfigurationTrigger trigger)
    {
        var (pause, _) = CreatePause(() => T0);

        Assert.True(pause.RunPause(trigger, () => { }).IsCompleted);
    }

    [Fact]
    public void RunPause_WithUndefinedTrigger_IsRejected()
    {
        var (pause, log) = CreatePause(() => T0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            pause.RunPause((ReconfigurationTrigger)99, () => { }));
        Assert.Empty(log);
        Assert.Equal(0, pause.PauseCount);
    }

    /// <summary>FR-015c: a fase InProgress é observável durante toda a pausa.</summary>
    [Fact]
    public void RunPause_SignalsInProgressBeforeCompleting()
    {
        var (pause, _) = CreatePause(() => T0);
        var phases = new List<ReconfigurationPhase>();
        pause.PhaseChanged += (_, args) => phases.Add(args.Phase);

        pause.RunPause(ReconfigurationTrigger.OpenFormatList, () =>
            Assert.Equal(ReconfigurationPhase.InProgress, pause.Phase));

        Assert.Equal(new[] { ReconfigurationPhase.InProgress, ReconfigurationPhase.Completed }, phases);
    }

    /// <summary>SC-004b: nada acontece sem um gatilho — a pausa não tem nenhum caminho periódico.</summary>
    [Fact]
    public void NoTrigger_MeansNoPause()
    {
        var (pause, log) = CreatePause(() => T0);

        Assert.Equal(0, pause.PauseCount);
        Assert.Null(pause.Phase);
        Assert.Empty(log);
    }
}
