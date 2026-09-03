using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica auto-detecção do M-Audio AIR, o filtro para dispositivos M-Audio apenas (o app é
/// específico para o AIR 192|4, não um seletor de entrada genérico), o comando de reinício de
/// conexão, e o fallback ao desconectar.
/// </summary>
public class DeviceSelectionIntegrationTests : IDisposable
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);
    private static readonly AudioInputDeviceInfo OtherDevice = new("other-id", "Built-in Microphone", 2, IsAirDevice: false);

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
    public void ResolveActiveDevice_WithAirPresent_AutoSelectsAir()
    {
        var (viewModel, engine, _, _) = CreateViewModel(AirDevice, OtherDevice);

        viewModel.ResolveActiveDevice();

        Assert.False(viewModel.NeedsSelection);
        Assert.Equal(AirDevice, viewModel.SelectedDevice);
        Assert.True(engine.IsStarted);
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);
    }

    [Fact]
    public void ResolveActiveDevice_WithNoAir_ExposesNeedsSelectionState()
    {
        var (viewModel, engine, _, _) = CreateViewModel(OtherDevice);

        viewModel.ResolveActiveDevice();

        Assert.True(viewModel.NeedsSelection);
        Assert.False(engine.IsStarted);
    }

    /// <summary>Só dispositivos M-Audio ficam disponíveis para seleção — o app é específico para o AIR 192|4.</summary>
    [Fact]
    public void AvailableDevices_OnlyIncludesAirDevices()
    {
        var (viewModel, _, _, _) = CreateViewModel(AirDevice, OtherDevice);

        viewModel.RefreshAvailableDevices();

        Assert.Equal(new[] { AirDevice }, viewModel.AvailableDevices);
    }

    /// <summary>Uma preferência de dispositivo não-M-Audio salva por uma versão anterior deixa de resolver, caindo para a auto-detecção do AIR.</summary>
    [Fact]
    public void ResolveActiveDevice_WithPersistedNonAirDevice_FallsBackToAirAutoDetection()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output", InputDeviceId = OtherDevice.Id });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice, OtherDevice });

        var viewModel = new InputDeviceSelectorViewModel(deviceProvider, engine, repository, "fake-output");
        viewModel.ResolveActiveDevice();

        Assert.False(viewModel.NeedsSelection);
        Assert.Equal(AirDevice, viewModel.SelectedDevice);
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);
    }

    [Fact]
    public void RestartConnection_StopsAndResolvesActiveDeviceAgain()
    {
        var (viewModel, engine, _, _) = CreateViewModel(AirDevice);
        viewModel.ResolveActiveDevice();
        Assert.True(engine.IsStarted);

        viewModel.RestartConnectionCommand.Execute(null);

        Assert.False(viewModel.NeedsSelection);
        Assert.Equal(AirDevice, viewModel.SelectedDevice);
        Assert.True(engine.IsStarted);
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);
    }

    /// <summary>
    /// Reproduz o bug reportado: IAudioEngine.Start lança (ex.: 0x88890008, formato não
    /// suportado) e mesmo assim as seções dependentes do dispositivo (formato de gravação,
    /// driver M-Audio) devem continuar aparecendo — antes da correção, a exceção não isolada
    /// impedia RefreshDeviceDependentSections de rodar, fazendo a seção do driver sumir por
    /// completo mesmo com o dispositivo corretamente reconhecido.
    /// </summary>
    [Fact]
    public void StartFailure_StillUpdatesDeviceDependentSections()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        engine.ForcedStartFailure = new InvalidOperationException("0x88890008");
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository, new FakeRecordingFormatController(), new FakeRecordingFormatRepository(), new FakeAsioSampleRateController());

        Assert.False(mainWindowViewModel.InputDeviceSelector.NeedsSelection);
        Assert.Equal(AirDevice, mainWindowViewModel.InputDeviceSelector.SelectedDevice);
        Assert.False(engine.IsStarted);
        Assert.True(mainWindowViewModel.DriverSettings.IsAirDeviceActive);
        Assert.Contains("0x88890008", mainWindowViewModel.CaptureFormatDescription);
    }

    /// <summary>
    /// Reproduz o bug reportado: com uma preferência de formato de gravação salva que diverge do
    /// atual, MainWindowViewModel.RefreshDeviceDependentSections (chamado depois que a captura já
    /// está rodando) não pode disparar uma segunda escrita — só a resolução pré-Start (via
    /// InputDeviceSelector.BeforeEngineStart) deve escrever. Antes da correção, a segunda escrita
    /// acontecia com a captura ativa e sem Stop/Start ao redor, travando os meters silenciosamente.
    /// </summary>
    [Fact]
    public void DeviceResolution_WithMismatchedRecordingFormat_WritesOnlyOnceBeforeStart()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);

        var recordingFormatController = new FakeRecordingFormatController();
        recordingFormatController.SetSupportedFormats(AirDevice.Id, new[] { new RecordingFormat(44100, 16), RecordingFormat.Default });
        // O dispositivo está atualmente em 44100/16, mas a preferência salva é o Default —
        // resolução pré-Start deve escrever o Default exatamente uma vez.
        recordingFormatController.SetCurrentFormat(AirDevice.Id, new RecordingFormat(44100, 16));
        var recordingFormatRepository = new FakeRecordingFormatRepository();
        recordingFormatRepository.Save(AirDevice.Id, RecordingFormat.Default);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository, recordingFormatController, recordingFormatRepository, new FakeAsioSampleRateController());

        Assert.Equal(1, recordingFormatController.TrySetFormatCallCount);
        Assert.True(engine.IsStarted);

        // Simula o que RefreshDeviceDependentSections faz de novo depois do Start (ex.: outro
        // evento de conexão) — não deve gerar uma segunda escrita nem parar o engine.
        mainWindowViewModel.RecordingFormatSelector.SyncDisplayOnly(mainWindowViewModel.InputDeviceSelector.SelectedDevice);

        Assert.Equal(1, recordingFormatController.TrySetFormatCallCount);
        Assert.True(engine.IsStarted);
    }

    [Fact]
    public void LiveDisconnect_OfAirDevice_ExposesNeedsSelectionWithoutRestartingApp()
    {
        ISettingsRepository repository = new SettingsRepository(_tempFilePath);
        repository.Save(repository.Load() with { OutputDeviceId = "fake-output" });

        var engine = new FakeAudioEngine();
        var deviceProvider = new FakeAudioDeviceProvider();
        deviceProvider.SetInputDevices(new[] { AirDevice });
        deviceProvider.SimulateConnection(true);

        var mainWindowViewModel = new MainWindowViewModel(engine, deviceProvider, repository, new FakeRecordingFormatController(), new FakeRecordingFormatRepository(), new FakeAsioSampleRateController());
        Assert.Equal(AirDevice.Id, engine.InputDeviceId);

        deviceProvider.SimulateInputDevicesChanged(Array.Empty<AudioInputDeviceInfo>());

        Assert.True(mainWindowViewModel.InputDeviceSelector.NeedsSelection);
        Assert.False(engine.IsStarted);
    }
}
