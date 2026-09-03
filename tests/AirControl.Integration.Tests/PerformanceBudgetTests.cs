using System.Diagnostics;
using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica os orçamentos de performance de plan.md: SetTrim/SetMute/SetSolo -> LevelsChanged
/// em menos de 100ms (SC-002) e detecção de conexão/desconexão em menos de 3s (SC-005).
/// </summary>
public class PerformanceBudgetTests
{
    private const long LevelsChangedBudgetMs = 100;
    private const long ConnectionDetectionBudgetMs = 3000;

    [Fact]
    public void SetTrim_ToLevelsChanged_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");
        var samples = new float[] { 0.3f, -0.3f };

        var stopwatch = Stopwatch.StartNew();
        engine.SetTrim(InputChannelId.Input1, 3.0);
        engine.PushSamples(InputChannelId.Input1, samples);
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void SetMute_ToEffectiveAudibilityChange_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.SetMute(InputChannelId.Input1, true);
        var isAudible = engine.GetState(InputChannelId.Input1).IsEffectivelyAudible;
        stopwatch.Stop();

        Assert.False(isAudible);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void SetSolo_ToEffectiveAudibilityChange_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.SetSolo(InputChannelId.Input1, true);
        var input2Audible = engine.GetState(InputChannelId.Input2).IsEffectivelyAudible;
        stopwatch.Stop();

        Assert.False(input2Audible);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void SetRoutingMode_ToLevelsChanged_IsWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start(null, "fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.SetRoutingMode(RoutingMode.CombinedMono);
        engine.PushRoutedSamples(new float[] { 0.4f }, new float[] { 0.4f });
        stopwatch.Stop();

        Assert.Equal(RoutingMode.CombinedMono, engine.RoutingMode);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void DeviceSwitch_StopThenStart_ReflectsInLevelsChangedWithinBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("device-a", "fake-output");

        var stopwatch = Stopwatch.StartNew();
        engine.Stop();
        engine.Start("device-b", "fake-output");
        engine.PushRoutedSamples(new float[] { 0.4f }, new float[] { 0.4f });
        stopwatch.Stop();

        Assert.True(engine.IsStarted);
        Assert.Equal("device-b", engine.InputDeviceId);
        Assert.True(stopwatch.ElapsedMilliseconds < LevelsChangedBudgetMs);
    }

    [Fact]
    public void ConnectionChange_IsDetectedWithinBudget()
    {
        var deviceProvider = new FakeAudioDeviceProvider();
        DeviceConnectionChangedEventArgs? received = null;
        deviceProvider.ConnectionChanged += (_, args) => received = args;

        var stopwatch = Stopwatch.StartNew();
        deviceProvider.SimulateConnection(true);
        stopwatch.Stop();

        Assert.NotNull(received);
        Assert.True(received!.IsConnected);
        Assert.True(stopwatch.ElapsedMilliseconds < ConnectionDetectionBudgetMs);
    }

    /// <summary>
    /// Uma troca de formato de gravação exige parar/reiniciar a captura (FR-010) — reaproveita o
    /// mesmo orçamento de 3s já usado para reconexão de dispositivo como teto para monitoramento/
    /// metering voltarem a funcionar (SC-005).
    /// </summary>
    [Fact]
    public void RecordingFormatChange_EngineRestartIsWithinConnectionBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("air-id", "fake-output");

        var controller = new FakeRecordingFormatController();
        var supported = new[] { new RecordingFormat(44100, 16), RecordingFormat.Default };
        controller.SetSupportedFormats("air-id", supported);
        var repository = new FakeRecordingFormatRepository();
        var viewModel = new RecordingFormatSelectorViewModel(controller, repository, engine, new FakeAsioSampleRateController(), "fake-output");
        viewModel.ResolveForDevice(new AudioInputDeviceInfo("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true));

        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedFormat = new RecordingFormat(44100, 16);
        stopwatch.Stop();

        Assert.True(engine.IsStarted);
        Assert.True(stopwatch.ElapsedMilliseconds < ConnectionDetectionBudgetMs);
    }

    /// <summary>
    /// SC-004a (feature 004): uma pausa de reconfiguração vai do início ao restabelecimento da
    /// captura em ≤ 2s. Mede o caminho completo Stop→mutar→Start da troca de formato.
    /// </summary>
    [Fact]
    public void ReconfigurationPause_FormatChange_IsWithinPauseBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("air-id", "fake-output");

        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats("air-id", new[] { new RecordingFormat(44100, 16), RecordingFormat.Default });
        var viewModel = new RecordingFormatSelectorViewModel(
            controller, new FakeRecordingFormatRepository(), engine, new FakeAsioSampleRateController(), "fake-output");
        viewModel.ResolveForDevice(new AudioInputDeviceInfo("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true));

        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedFormat = new RecordingFormat(44100, 16);
        stopwatch.Stop();

        Assert.Equal(ReconfigurationPhase.Completed, viewModel.Pause.Phase);
        Assert.True(engine.IsStarted);
        Assert.True(stopwatch.Elapsed < ReconfigurationPause.DefaultDeadline);
    }

    /// <summary>SC-004a: o mesmo teto de 2s vale para a pausa da troca de sample rate do driver.</summary>
    [Fact]
    public void ReconfigurationPause_DriverSampleRateChange_IsWithinPauseBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("air-id", "fake-output");

        var asioController = new FakeAsioSampleRateController();
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        asioController.SetCurrentSampleRate(48000);
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(new AudioInputDeviceInfo("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true));

        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedSampleRate = 44100;
        stopwatch.Stop();

        Assert.Equal(ReconfigurationPhase.Completed, viewModel.Pause.Phase);
        Assert.True(engine.IsStarted);
        Assert.True(stopwatch.Elapsed < ReconfigurationPause.DefaultDeadline);
    }

    /// <summary>
    /// SC-003: depois de qualquer alteração de configuração do usuário, a monitoração volta a
    /// operar (níveis chegando de novo) dentro do mesmo teto de 3s já usado para reconexão.
    /// </summary>
    [Fact]
    public void PostChangeRecovery_ToLevelsChanged_IsWithinConnectionBudget()
    {
        var engine = new FakeAudioEngine();
        engine.Start("air-id", "fake-output");

        var asioController = new FakeAsioSampleRateController();
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        asioController.SetCurrentSampleRate(48000);
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(new AudioInputDeviceInfo("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true));

        var levelsReceived = 0;
        engine.LevelsChanged += (_, _) => levelsReceived++;

        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedSampleRate = 44100;
        engine.PushRoutedSamples(new float[] { 0.4f }, new float[] { 0.4f });
        stopwatch.Stop();

        Assert.True(levelsReceived > 0);
        Assert.True(stopwatch.ElapsedMilliseconds < ConnectionDetectionBudgetMs);
    }
}
