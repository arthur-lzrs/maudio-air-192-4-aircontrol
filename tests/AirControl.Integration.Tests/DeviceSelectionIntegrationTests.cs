using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica auto-detecção do M-Audio AIR, seleção manual, persistência, fallback em desconexão
/// (US3, FR-007 a FR-012) e a revalidação de RoutingMode ao trocar de dispositivo (FR-005).
/// </summary>
public class DeviceSelectionIntegrationTests : IDisposable
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);
    private static readonly AudioInputDeviceInfo OtherDevice = new("other-id", "Built-in Microphone", 2, IsAirDevice: false);
    private static readonly AudioInputDeviceInfo MonoDevice = new("mono-id", "USB Mono Mic", 1, IsAirDevice: false);

    private readonly string _tempFilePath;

    public DeviceSelectionIntegrationTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"air-control-device-tests-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    private (InputDeviceSelectorViewModel ViewModel, FakeAudioEngine Engine, FakeAudioDeviceProvider DeviceProvider, ISettingsRepository Repository)
        CreateViewModel(params AudioInputDeviceInfo[] devices)
    {
        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(devices);
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);

        var viewModel = new InputDeviceSelectorViewModel(deviceProvider, engine, repository, "fake-output");
        return (viewModel, engine, deviceProvider, repository);
    }

    [Fact]
    public void ResolveActiveDevice_WithAirPresentAndNoManualSelection_AutoSelectsAir()
    {
        var (viewModel, engine, _, _) = CreateViewModel(AirDevice, OtherDevice);

        viewModel.ResolveActiveDevice();

        Assert.False(viewModel.NeedsSelection);
        Assert.Equal(AirDevice, viewModel.SelectedDevice);
        Assert.True(engine.IsStarted);
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);
    }

    [Fact]
    public void ResolveActiveDevice_WithNoAirAndNoManualSelection_ExposesNeedsSelectionState()
    {
        var (viewModel, engine, _, _) = CreateViewModel(OtherDevice);

        viewModel.ResolveActiveDevice();

        Assert.True(viewModel.NeedsSelection);
        Assert.False(engine.IsStarted);
    }

    [Fact]
    public void ManualSelection_SwitchesActiveChannelsToSelectedDevice()
    {
        var (viewModel, engine, _, _) = CreateViewModel(AirDevice, OtherDevice);
        viewModel.ResolveActiveDevice();

        viewModel.SelectedDevice = OtherDevice;

        Assert.True(engine.IsStarted);
        Assert.Equal(OtherDevice.Id, engine.InputDeviceId);
    }

    [Fact]
    public void ManualSelection_PersistsAndIsRestoredOnRestart_WhileStillConnected()
    {
        var (viewModel, _, _, repository) = CreateViewModel(AirDevice, OtherDevice);
        viewModel.ResolveActiveDevice();
        viewModel.SelectedDevice = OtherDevice;

        var (restartedViewModel, restartedEngine, _, _) = CreateViewModel(AirDevice, OtherDevice);
        restartedViewModel.ResolveActiveDevice();

        Assert.Equal(OtherDevice, restartedViewModel.SelectedDevice);
        Assert.Equal(OtherDevice.Id, restartedEngine.InputDeviceId);
        Assert.Equal(OtherDevice.Id, repository.Load().InputDeviceId);
    }

    [Fact]
    public void ManualSelection_WhenDeviceDisconnects_FallsBackToAirAutoDetection()
    {
        var (viewModel, _, _, _) = CreateViewModel(AirDevice, OtherDevice);
        viewModel.ResolveActiveDevice();
        viewModel.SelectedDevice = OtherDevice;

        var (restartedViewModel, restartedEngine, _, _) = CreateViewModel(AirDevice);
        restartedViewModel.ResolveActiveDevice();

        Assert.False(restartedViewModel.NeedsSelection);
        Assert.Equal(AirDevice, restartedViewModel.SelectedDevice);
        Assert.Equal(AirDevice.Id, restartedEngine.InputDeviceId);
    }

    [Fact]
    public void ManualSelection_WhenDeviceDisconnectsAndNoAir_PromptsForSelection()
    {
        var (viewModel, _, _, _) = CreateViewModel(AirDevice, OtherDevice);
        viewModel.ResolveActiveDevice();
        viewModel.SelectedDevice = OtherDevice;

        var (restartedViewModel, restartedEngine, _, _) = CreateViewModel();
        restartedViewModel.ResolveActiveDevice();

        Assert.True(restartedViewModel.NeedsSelection);
        Assert.False(restartedEngine.IsStarted);
    }

    [Fact]
    public void LiveDisconnect_OfManuallySelectedDevice_FallsBackToAirWithoutRestartingApp()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice, OtherDevice });
        deviceProvider.SimulateConnection(true);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository);
        mainWindowViewModel.InputDeviceSelector.SelectedDevice = OtherDevice;
        Assert.Equal(OtherDevice.Id, engine.InputDeviceId);

        deviceProvider.SimulateInputDevicesChanged(new[] { AirDevice });

        Assert.False(mainWindowViewModel.InputDeviceSelector.NeedsSelection);
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);
        Assert.True(engine.IsStarted);
    }

    [Fact]
    public void Startup_WithoutAirConnected_RestoresPersistedManualSelectionForStillConnectedDevice()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output", InputDeviceId = OtherDevice.Id });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { OtherDevice });
        // O AIR não está conectado no lançamento; apenas o dispositivo manualmente selecionado está.
        deviceProvider.SimulateConnection(false);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository);

        Assert.False(mainWindowViewModel.InputDeviceSelector.NeedsSelection);
        Assert.Equal(OtherDevice, mainWindowViewModel.InputDeviceSelector.SelectedDevice);
        Assert.True(engine.IsStarted);
        Assert.Equal(OtherDevice.Id, engine.InputDeviceId);
    }

    [Fact]
    public void Startup_WithoutAirConnectedAndNoValidPersistedSelection_ExposesNeedsSelectionState()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { OtherDevice });
        deviceProvider.SimulateConnection(false);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository);

        Assert.True(mainWindowViewModel.InputDeviceSelector.NeedsSelection);
        Assert.False(engine.IsStarted);
    }

    [Fact]
    public void SwitchingToOneChannelDevice_WhileMultiChannelRoutingActive_FallsBackToInput1Mono()
    {
        var (viewModel, engine, _, _) = CreateViewModel(AirDevice, MonoDevice);
        engine.SetChannelCountForDevice(MonoDevice.Id, MonoDevice.ChannelCount);
        viewModel.ResolveActiveDevice();
        engine.SetRoutingMode(RoutingMode.CombinedMono);

        viewModel.SelectedDevice = MonoDevice;

        Assert.Equal(1, engine.ActiveInputChannelCount);
        Assert.Equal(RoutingMode.Input1Mono, engine.RoutingMode);
    }
}
