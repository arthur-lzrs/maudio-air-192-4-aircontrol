using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// SC-001 / FR-002–FR-005: toda abertura do app tem que chegar ao MESMO estado funcional. Cobre a
/// regressão de S1 (seletor de roteamento vazio e silencioso quando <c>ActiveInputChannelCount == 0</c>)
/// e de S6 (notificação de dispositivo chegando logo depois da resolução do fim do ctor tem que
/// produzir o mesmo estado final — idempotência).
/// </summary>
public class StartupDeterminismIntegrationTests : IDisposable
{
    private const int StartupCycles = 20;

    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);

    private readonly string _tempFilePath =
        Path.Combine(Path.GetTempPath(), $"air-control-startup-determinism-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private sealed record StartupSnapshot(
        bool NeedsSelection,
        string AvailableModes,
        RoutingMode SelectedMode,
        bool RoutingIsDeterminable,
        bool RoutingHasMessage,
        bool IsAirDeviceActive);

    private static StartupSnapshot Snapshot(MainWindowViewModel viewModel) => new(
        viewModel.InputDeviceSelector.NeedsSelection,
        string.Join(",", viewModel.RoutingModeSelector.AvailableModes),
        viewModel.RoutingModeSelector.SelectedMode,
        viewModel.RoutingModeSelector.IsDeterminable,
        !string.IsNullOrWhiteSpace(viewModel.RoutingModeSelector.StatusMessage),
        viewModel.DriverSettings.IsAirDeviceActive);

    private ISettingsRepository CreateRepository(RoutingMode persistedMode)
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output", RoutingMode = persistedMode });
        return repository;
    }

    private MainWindowViewModel CreateMainWindow(
        FakeAudioEngine engine,
        FakeAudioDeviceProvider deviceProvider,
        ISettingsRepository repository) =>
        new(
            engine,
            deviceProvider,
            repository,
            new FakeRecordingFormatController(),
            new FakeRecordingFormatRepository(),
            new FakeAsioSampleRateController());

    /// <summary>20 aberturas com um AIR normal (2 canais) produzem exatamente o mesmo estado final.</summary>
    [Fact]
    public void TwentyStartups_WithHealthyDevice_ProduceIdenticalFinalState()
    {
        var repository = CreateRepository(RoutingMode.Input2Mono);
        var snapshots = new List<StartupSnapshot>();

        for (var i = 0; i < StartupCycles; i++)
        {
            var engine = new FakeAudioEngine();
            var deviceProvider = new FakeAudioDeviceProvider();
            deviceProvider.SetInputDevices(new[] { AirDevice });
            deviceProvider.SimulateConnection(true);

            snapshots.Add(Snapshot(CreateMainWindow(engine, deviceProvider, repository)));
        }

        Assert.Single(snapshots.Distinct());
        var final = snapshots[0];
        Assert.True(final.RoutingIsDeterminable);
        Assert.NotEmpty(final.AvailableModes);
        Assert.Equal(RoutingMode.Input2Mono, final.SelectedMode);
    }

    /// <summary>
    /// Regressão de S1: com o transiente <c>ActiveInputChannelCount == 0</c> (Start "bem-sucedido"
    /// mas com canais indetermináveis), o seletor NUNCA pode ficar vazio sem mensagem — e a
    /// seleção persistida tem que sobreviver ao transiente (FR-002/FR-003/SC-001).
    /// </summary>
    [Fact]
    public void TwentyStartups_WithZeroChannelTransient_NeverLeaveRoutingSilentlyEmpty()
    {
        var repository = CreateRepository(RoutingMode.Input2Mono);
        var snapshots = new List<StartupSnapshot>();

        for (var i = 0; i < StartupCycles; i++)
        {
            var engine = new FakeAudioEngine();
            engine.SetChannelCountForDevice(AirDevice.Id, 0);
            var deviceProvider = new FakeAudioDeviceProvider();
            deviceProvider.SetInputDevices(new[] { AirDevice });
            deviceProvider.SimulateConnection(true);

            var viewModel = CreateMainWindow(engine, deviceProvider, repository);

            Assert.False(viewModel.RoutingModeSelector.IsDeterminable);
            Assert.Empty(viewModel.RoutingModeSelector.AvailableModes);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.RoutingModeSelector.StatusMessage));
            Assert.Equal(RoutingMode.Input2Mono, viewModel.RoutingModeSelector.SelectedMode);

            snapshots.Add(Snapshot(viewModel));
        }

        Assert.Single(snapshots.Distinct());
    }

    /// <summary>FR-004: um dispositivo válido chegando depois da abertura repopula o seletor sozinho.</summary>
    [Fact]
    public void DeviceArrivingAfterOpen_RepopulatesRoutingOptions()
    {
        var repository = CreateRepository(RoutingMode.CombinedMono);
        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(Array.Empty<AudioInputDeviceInfo>());

        var viewModel = CreateMainWindow(engine, deviceProvider, repository);

        Assert.True(viewModel.InputDeviceSelector.NeedsSelection);
        Assert.False(viewModel.RoutingModeSelector.IsDeterminable);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.RoutingModeSelector.StatusMessage));

        deviceProvider.SimulateInputDevicesChanged(new[] { AirDevice });

        Assert.False(viewModel.InputDeviceSelector.NeedsSelection);
        Assert.True(viewModel.RoutingModeSelector.IsDeterminable);
        Assert.NotEmpty(viewModel.RoutingModeSelector.AvailableModes);
        Assert.Null(viewModel.RoutingModeSelector.StatusMessage);
        Assert.Equal(RoutingMode.CombinedMono, viewModel.RoutingModeSelector.SelectedMode);
    }

    /// <summary>
    /// Regressão de S6: a resolução do fim do ctor roda depois de todos os handlers fiados, e uma
    /// segunda notificação chegando logo em seguida produz exatamente o mesmo estado final
    /// (idempotência — FR-005, research.md §5).
    /// </summary>
    [Fact]
    public void SecondNotificationRightAfterStartup_ProducesIdenticalState()
    {
        var repository = CreateRepository(RoutingMode.Input1Mono);
        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);

        var viewModel = CreateMainWindow(engine, deviceProvider, repository);
        var afterStartup = Snapshot(viewModel);

        deviceProvider.SimulateConnection(true);
        deviceProvider.SimulateInputDevicesChanged();

        Assert.Equal(afterStartup, Snapshot(viewModel));
        Assert.True(engine.IsStarted);
    }
}
