using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Regressão de S2 (research.md §1) / SC-002 / FR-006–FR-009: um fluxo que para tem que virar um
/// estado observável — recuperação automática limitada ou erro acionável — nunca um congelamento
/// silencioso com o medidor preso no último valor.
/// </summary>
public class StreamHealthIntegrationTests : IDisposable
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);

    private readonly string _tempFilePath =
        Path.Combine(Path.GetTempPath(), $"air-control-stream-health-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private MainWindowViewModel CreateMainWindow(FakeAudioEngine engine, FakeAudioDeviceProvider deviceProvider)
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        return new MainWindowViewModel(
            engine,
            deviceProvider,
            repository,
            new FakeRecordingFormatController(),
            new FakeRecordingFormatRepository(),
            new FakeAsioSampleRateController());
    }

    private static (FakeAudioEngine Engine, FakeAudioDeviceProvider DeviceProvider) CreateStartedEngine()
    {
        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);
        return (engine, deviceProvider);
    }

    /// <summary>Parada simulada com recuperação possível → volta a Delivering dentro do teto de tentativas.</summary>
    [Fact]
    public void SimulatedStall_WithRecoverableStream_ReturnsToDelivering()
    {
        var (engine, deviceProvider) = CreateStartedEngine();
        CreateMainWindow(engine, deviceProvider);

        engine.SimulateStreamStopped("WasapiCapture.RecordingStopped: dispositivo suspenso");

        Assert.Equal(AudioStreamState.Delivering, engine.Health.State);
        Assert.True(engine.IsStarted);
        Assert.Null(engine.Health.FaultReason);
    }

    /// <summary>Parada com recuperação impossível → no máximo 2 tentativas e Faulted com motivo acionável.</summary>
    [Fact]
    public void SimulatedStall_WithUnrecoverableStream_FaultsAfterBoundedAttempts()
    {
        var (engine, deviceProvider) = CreateStartedEngine();
        CreateMainWindow(engine, deviceProvider);
        engine.RecoveryRestartSucceeds = false;

        engine.SimulateStreamStopped("WasapiCapture.RecordingStopped: dispositivo removido");

        Assert.Equal(AudioStreamState.Faulted, engine.Health.State);
        Assert.Equal(2, engine.Health.RecoveryAttempts);
        Assert.False(string.IsNullOrWhiteSpace(engine.Health.FaultReason));
    }

    /// <summary>O watchdog compara agora - LastDataReceivedAt (sem polling do driver) e sinaliza a parada.</summary>
    [Fact]
    public void Watchdog_WithNoDataBeyondThreshold_SignalsStalled()
    {
        var (engine, deviceProvider) = CreateStartedEngine();
        CreateMainWindow(engine, deviceProvider);
        engine.RecoveryRestartSucceeds = false;

        var now = DateTimeOffset.UtcNow;
        engine.Clock = () => now;
        engine.SimulateDataReceived();

        var states = new List<AudioStreamState>();
        engine.StreamHealthChanged += (_, args) => states.Add(args.State);

        now = now.AddSeconds(6);
        engine.RunWatchdogTick();

        Assert.Contains(AudioStreamState.Stalled, states);
    }

    /// <summary>FR-007 / Constitution III: Faulted vira mensagem acionável na UI; Delivering a limpa.</summary>
    [Fact]
    public void FaultedStream_SurfacesActionableStatusAndClearsOnRecovery()
    {
        var (engine, deviceProvider) = CreateStartedEngine();
        var viewModel = CreateMainWindow(engine, deviceProvider);
        engine.RecoveryRestartSucceeds = false;

        engine.SimulateStreamStopped("dispositivo removido");

        Assert.False(string.IsNullOrWhiteSpace(viewModel.StreamHealthMessage));
        Assert.Contains(engine.Health.FaultReason!, viewModel.StreamHealthMessage!);

        engine.RecoveryRestartSucceeds = true;
        engine.SimulateDataReceived();

        Assert.Equal(AudioStreamState.Delivering, engine.Health.State);
        Assert.Null(viewModel.StreamHealthMessage);
    }

    /// <summary>
    /// Contra-exemplo do contrato: o medidor NÃO pode segurar o último valor através de uma
    /// transição Stalled/Faulted — ele volta ao estado de repouso para não mentir sobre o sinal.
    /// </summary>
    [Fact]
    public void ChannelMeter_DoesNotHoldAFrozenValueAcrossAStall()
    {
        var (engine, deviceProvider) = CreateStartedEngine();
        var viewModel = CreateMainWindow(engine, deviceProvider);
        engine.RecoveryRestartSucceeds = false;

        engine.PushRoutedSamples(new float[] { 0.9f }, new float[] { 0.9f });
        Assert.True(viewModel.Input1Meter.PeakDb > LevelMetering.SilenceFloorDb);

        engine.SimulateStreamStopped("dispositivo removido");

        Assert.Equal(LevelMetering.SilenceFloorDb, viewModel.Input1Meter.PeakDb);
        Assert.Equal(LevelMetering.SilenceFloorDb, viewModel.Input2Meter.RmsDb);
        Assert.False(viewModel.Input1Meter.IsClipping);
    }

    /// <summary>Regra 5 do contrato: StreamHealthChanged é entregue na thread da UI.</summary>
    [Fact]
    public void StreamHealthChanged_RaisedFromWorkerThread_IsDeliveredOnTheUiThread()
    {
        using var ui = new UiThreadHarness();
        var engine = new FakeAudioEngine(ui.Dispatcher);
        engine.Start(AirDevice.Id, "fake-output");
        engine.RecoveryRestartSucceeds = false;

        var deliveryThreadIds = new List<int>();
        engine.StreamHealthChanged += (_, _) => deliveryThreadIds.Add(Environment.CurrentManagedThreadId);

        UiThreadHarness.RunOnWorkerThread(() => engine.SimulateStreamStopped("parada externa"));
        ui.Drain();

        Assert.NotEmpty(deliveryThreadIds);
        Assert.All(deliveryThreadIds, id => Assert.Equal(ui.UiThreadId, id));
    }

    /// <summary>
    /// SC-002: a detecção de congelamento acontece em ≤ 5s do último dado — o limiar default é a
    /// política pura, sem nenhum polling do driver (FR-015b/SC-004b).
    /// </summary>
    [Fact]
    public void StalenessDetection_HappensWithinFiveSecondsOfTheLastData()
    {
        var health = new AudioStreamHealth();
        var t0 = DateTimeOffset.UtcNow;
        health.MarkDataReceived(t0);

        Assert.False(health.EvaluateStaleness(t0.AddSeconds(4.9)));
        Assert.True(health.EvaluateStaleness(t0.AddSeconds(5)));
        Assert.True(health.StalenessThreshold <= TimeSpan.FromSeconds(5));
    }
}
