using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica a seção "Driver M-Audio" (US3, FR-007 a FR-009): diagnóstico e visibilidade
/// atrelada ao dispositivo M-Audio ativo, mesma regra de <see cref="RecordingFormatSelectorViewModel"/>
/// (research.md §7). Não há caminho de controle inline nesta iteração (research.md §6).
/// </summary>
public class DriverSettingsIntegrationTests
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);
    private static readonly AudioInputDeviceInfo OtherDevice = new("other-id", "Built-in Microphone", 2, IsAirDevice: false);

    [Fact]
    public void UpdateForDevice_IsVisibleOnlyWhenActiveDeviceIsAir()
    {
        var engine = new FakeAudioEngine();
        var viewModel = new DriverSettingsViewModel(engine, new FakeAsioSampleRateController(), "fake-output");

        viewModel.UpdateForDevice(OtherDevice);
        Assert.False(viewModel.IsAirDeviceActive);

        viewModel.UpdateForDevice(AirDevice);
        Assert.True(viewModel.IsAirDeviceActive);
    }

    [Fact]
    public void UpdateForDevice_ReflectsCaptureFormatDescription_WhenAirDeviceActive()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var viewModel = new DriverSettingsViewModel(engine, new FakeAsioSampleRateController(), "fake-output");

        viewModel.UpdateForDevice(AirDevice);

        Assert.Equal(engine.CaptureFormatDescription, viewModel.DiagnosticInfo);
    }

    [Fact]
    public void UpdateForDevice_ClearsDiagnosticInfo_WhenActiveDeviceIsNotAir()
    {
        var engine = new FakeAudioEngine();
        engine.Start(OtherDevice.Id, "fake-output");
        var viewModel = new DriverSettingsViewModel(engine, new FakeAsioSampleRateController(), "fake-output");

        viewModel.UpdateForDevice(OtherDevice);

        Assert.Null(viewModel.DiagnosticInfo);
    }

    [Fact]
    public void OpenManufacturerPanel_WhenExecutableCannotBeFound_SurfacesActionableMessage()
    {
        var engine = new FakeAudioEngine();
        // Resolver determinístico simulando "instalador não encontrado" — não depende do
        // sistema de arquivos real, que pode ter o driver de fato instalado na máquina de teste.
        var viewModel = new DriverSettingsViewModel(engine, new FakeAsioSampleRateController(), "fake-output", resolvePanelPath: () => null);
        viewModel.UpdateForDevice(AirDevice);

        viewModel.OpenManufacturerPanelCommand.Execute(null);

        Assert.False(string.IsNullOrWhiteSpace(viewModel.StatusMessage));
    }

    [Fact]
    public void UpdateForDevice_ExposesCurrentAndSupportedAsioSampleRates_WhenAirDeviceActive()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(48000);
        asioController.SetSupportedSampleRates(new[] { 44100, 48000, 96000 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");

        viewModel.UpdateForDevice(AirDevice);

        Assert.Equal(48000, viewModel.SelectedSampleRate);
        Assert.Equal(new[] { 44100, 48000, 96000 }, viewModel.AvailableSampleRates);
    }

    [Fact]
    public void SelectedSampleRateChanged_ToSupportedRate_AppliesItAndRestartsEngine()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(44100);
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(AirDevice);

        viewModel.SelectedSampleRate = 48000;

        Assert.Equal(48000, asioController.GetCurrentSampleRate());
        Assert.True(engine.IsStarted);
        Assert.Null(viewModel.StatusMessage);
    }

    [Fact]
    public void SelectedSampleRateChanged_ToUnsupportedRate_IsRejectedAndKeepsPreviousRateActive()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(44100);
        asioController.SetSupportedSampleRates(new[] { 44100 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(AirDevice);

        viewModel.SelectedSampleRate = 192000;

        Assert.Equal(44100, asioController.GetCurrentSampleRate());
        Assert.NotNull(viewModel.StatusMessage);
    }

    [Fact]
    public void UpdateForDevice_ClearsAsioSampleRateControls_WhenActiveDeviceIsNotAir()
    {
        var engine = new FakeAudioEngine();
        var asioController = new FakeAsioSampleRateController();
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(AirDevice);

        viewModel.UpdateForDevice(OtherDevice);

        Assert.Empty(viewModel.AvailableSampleRates);
        Assert.Null(viewModel.SelectedSampleRate);
    }

    [Fact]
    public void UpdateSampleRateMismatch_WithDifferentAsioAndWindowsRates_SurfacesWarning()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(48000);
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(AirDevice);

        viewModel.UpdateSampleRateMismatch(windowsSampleRate: 44100);

        Assert.NotNull(viewModel.SampleRateMismatchWarning);
    }

    [Fact]
    public void UpdateSampleRateMismatch_WithMatchingRates_ClearsWarning()
    {
        var engine = new FakeAudioEngine();
        engine.Start(AirDevice.Id, "fake-output");
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(48000);
        asioController.SetSupportedSampleRates(new[] { 44100, 48000 });
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(AirDevice);
        viewModel.UpdateSampleRateMismatch(windowsSampleRate: 44100);
        Assert.NotNull(viewModel.SampleRateMismatchWarning);

        viewModel.UpdateSampleRateMismatch(windowsSampleRate: 48000);

        Assert.Null(viewModel.SampleRateMismatchWarning);
    }

    [Fact]
    public void UpdateSampleRateMismatch_WhenActiveDeviceIsNotAir_NeverWarns()
    {
        var engine = new FakeAudioEngine();
        var asioController = new FakeAsioSampleRateController();
        var viewModel = new DriverSettingsViewModel(engine, asioController, "fake-output");
        viewModel.UpdateForDevice(OtherDevice);

        viewModel.UpdateSampleRateMismatch(windowsSampleRate: 44100);

        Assert.Null(viewModel.SampleRateMismatchWarning);
    }
}
