using System.Diagnostics;
using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Regressão de S3/S5 (research.md §1) e verificação de SC-004/SC-004a/SC-004b: a troca de formato e
/// a troca de sample rate do driver restabelecem a captura dentro do teto, a consulta ao driver
/// acontece só dentro da pausa, e nada dispara uma pausa sem uma ação discreta do usuário.
/// </summary>
public class ReconfigurationPauseIntegrationTests : IDisposable
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);

    private static readonly RecordingFormat[] SupportedFormats =
    {
        new(44100, 16), new(44100, 24), new(48000, 16), RecordingFormat.Default,
    };

    private readonly string _tempFilePath =
        Path.Combine(Path.GetTempPath(), $"air-control-reconfig-pause-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private (MainWindowViewModel ViewModel, FakeAudioEngine Engine, FakeRecordingFormatController FormatController, FakeAsioSampleRateController AsioController)
        CreateMainWindow(int? asioSampleRate = 48000)
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);

        var formatController = new FakeRecordingFormatController();
        formatController.SetSupportedFormats(AirDevice.Id, SupportedFormats);
        formatController.SetCurrentFormat(AirDevice.Id, RecordingFormat.Default);

        var asioController = new FakeAsioSampleRateController();
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        asioController.SetCurrentSampleRate(asioSampleRate);

        var viewModel = new MainWindowViewModel(
            engine,
            deviceProvider,
            repository,
            formatController,
            new FakeRecordingFormatRepository(),
            asioController);

        return (viewModel, engine, formatController, asioController);
    }

    /// <summary>SC-004a/SC-003: a troca de formato restabelece a captura dentro do teto de 2s.</summary>
    [Fact]
    public void FormatChange_ReestablishesCaptureWithinTheDeadline()
    {
        var (viewModel, engine, _, _) = CreateMainWindow();

        var stopwatch = Stopwatch.StartNew();
        viewModel.RecordingFormatSelector.SelectedFormat = new RecordingFormat(44100, 16);
        stopwatch.Stop();

        Assert.True(engine.IsStarted);
        Assert.Equal(ReconfigurationPhase.Completed, viewModel.RecordingFormatSelector.Pause.Phase);
        Assert.Null(viewModel.ReconfigurationMessage);
        Assert.True(stopwatch.Elapsed < ReconfigurationPause.DefaultDeadline);
    }

    /// <summary>SC-004a: a troca de sample rate do driver restabelece a captura dentro do teto.</summary>
    [Fact]
    public void DriverSampleRateChange_ReestablishesCaptureWithinTheDeadline()
    {
        var (viewModel, engine, _, _) = CreateMainWindow();

        var stopwatch = Stopwatch.StartNew();
        viewModel.DriverSettings.SelectedSampleRate = 44100;
        stopwatch.Stop();

        Assert.True(engine.IsStarted);
        Assert.Equal(ReconfigurationPhase.Completed, viewModel.DriverSettings.Pause.Phase);
        Assert.True(stopwatch.Elapsed < ReconfigurationPause.DefaultDeadline);
    }

    /// <summary>
    /// Regressão de S5: uma mutação que lança (escrita de formato falhando com exceção) NÃO pode
    /// deixar a captura parada — o Start de restauração roda em finally.
    /// </summary>
    [Fact]
    public void FormatChange_WithThrowingWrite_StillLeavesCaptureRunning()
    {
        var (viewModel, engine, formatController, _) = CreateMainWindow();
        formatController.ForcedTrySetFormatException = new InvalidOperationException("escrita falhou");

        viewModel.RecordingFormatSelector.SelectedFormat = new RecordingFormat(44100, 16);

        Assert.True(engine.IsStarted);
        Assert.Equal(ReconfigurationPhase.Faulted, viewModel.RecordingFormatSelector.Pause.Phase);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ReconfigurationMessage));
    }

    /// <summary>Regressão de S5 no outro call site: handshake ASIO que lança não pode matar a captura.</summary>
    [Fact]
    public void DriverSampleRateChange_WithThrowingHandshake_StillLeavesCaptureRunning()
    {
        var (viewModel, engine, _, asioController) = CreateMainWindow();
        asioController.ForcedTrySetSampleRateException = new InvalidOperationException("handshake falhou");

        viewModel.DriverSettings.SelectedSampleRate = 44100;

        Assert.True(engine.IsStarted);
        Assert.Equal(ReconfigurationPhase.Faulted, viewModel.DriverSettings.Pause.Phase);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ReconfigurationMessage));
    }

    /// <summary>
    /// Regressão de S3/R3: nenhuma consulta ao driver ASIO acontece com a captura ativa. A consulta
    /// em tempo real só ocorre dentro da pausa, disparada por "abrir a lista de formatos".
    /// </summary>
    [Fact]
    public void AsioQuery_OnlyHappensInsideAPause()
    {
        var (viewModel, engine, _, asioController) = CreateMainWindow();
        var queriesDuringStartup = asioController.GetCurrentSampleRateCallCount;

        var wasStoppedDuringQuery = false;
        asioController.OnGetCurrentSampleRate = () => wasStoppedDuringQuery = !engine.IsStarted;

        viewModel.RecordingFormatSelector.RefreshFormatOptionsFromDriverCommand.Execute(null);

        Assert.True(asioController.GetCurrentSampleRateCallCount > queriesDuringStartup);
        Assert.True(wasStoppedDuringQuery);
        Assert.True(engine.IsStarted);
    }

    /// <summary>SC-004: a lista de formatos só oferece combinações do sample rate atual do driver.</summary>
    [Fact]
    public void RefreshFromDriver_LimitsFormatsToTheDriverSampleRate()
    {
        var (viewModel, _, _, _) = CreateMainWindow(asioSampleRate: 44100);

        viewModel.RecordingFormatSelector.RefreshFormatOptionsFromDriverCommand.Execute(null);

        Assert.All(viewModel.RecordingFormatSelector.AvailableFormats, format => Assert.Equal(44100, format.SampleRate));
    }

    /// <summary>
    /// FR-012/FR-013: mudar o sample rate do driver repopula as opções na hora e reconcilia o
    /// formato atual, reportando o que foi aplicado.
    /// </summary>
    [Fact]
    public void DriverSampleRateChange_RefreshesAndReconcilesTheRecordingFormat()
    {
        var (viewModel, _, _, _) = CreateMainWindow(asioSampleRate: 48000);
        Assert.Equal(48000, viewModel.RecordingFormatSelector.SelectedFormat?.SampleRate);

        viewModel.DriverSettings.SelectedSampleRate = 44100;

        Assert.All(viewModel.RecordingFormatSelector.AvailableFormats, format => Assert.Equal(44100, format.SampleRate));
        Assert.Equal(44100, viewModel.RecordingFormatSelector.SelectedFormat?.SampleRate);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.RecordingFormatSelector.StatusMessage));
    }

    /// <summary>
    /// SC-004b: 30 min sem tocar em formato/driver ⇒ ZERO pausas. Simula a passagem de tempo com
    /// tudo o que roda sozinho (níveis chegando, ticks de watchdog, notificações de dispositivo
    /// irrelevantes) e confirma que nenhuma pausa foi disparada — não existe caminho periódico.
    /// </summary>
    [Fact]
    public void ThirtyIdleMinutes_ProduceZeroReconfigurationPauses()
    {
        var (viewModel, engine, _, asioController) = CreateMainWindow();
        var pausesAfterStartup =
            viewModel.RecordingFormatSelector.Pause.PauseCount + viewModel.DriverSettings.Pause.PauseCount;
        var asioQueriesAfterStartup = asioController.GetCurrentSampleRateCallCount;

        var now = DateTimeOffset.UtcNow;
        engine.Clock = () => now;

        // 30 minutos de operação normal, um "segundo" de cada vez (níveis + tick do watchdog).
        for (var second = 0; second < 30 * 60; second++)
        {
            now = now.AddSeconds(1);
            engine.PushRoutedSamples(new float[] { 0.2f }, new float[] { 0.2f });
            engine.SimulateDataReceived();
            engine.RunWatchdogTick();
        }

        Assert.Equal(
            pausesAfterStartup,
            viewModel.RecordingFormatSelector.Pause.PauseCount + viewModel.DriverSettings.Pause.PauseCount);
        Assert.Equal(asioQueriesAfterStartup, asioController.GetCurrentSampleRateCallCount);
        Assert.Equal(AudioStreamState.Delivering, engine.Health.State);
    }
}
