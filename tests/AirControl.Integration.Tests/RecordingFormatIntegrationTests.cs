using AirControl.App.ViewModels;
using AirControl.Core;
using AirControl.Integration.Tests.Fakes;
using Xunit;

namespace AirControl.Integration.Tests;

/// <summary>
/// Verifica a resolução/fallback/persistência do "Formato Padrão" do Windows (US2, FR-003 a
/// FR-006, FR-010) e a visibilidade dos controles atrelada ao dispositivo M-Audio ativo
/// (research.md §7), usando fakes — sem tocar hardware/COM real.
/// </summary>
public class RecordingFormatIntegrationTests
{
    private static readonly AudioInputDeviceInfo AirDevice = new("air-id", "M-Audio AIR 192|4", 2, IsAirDevice: true);
    private static readonly AudioInputDeviceInfo OtherDevice = new("other-id", "Built-in Microphone", 2, IsAirDevice: false);

    private static RecordingFormatSelectorViewModel CreateViewModel(
        FakeRecordingFormatController controller,
        FakeRecordingFormatRepository repository,
        FakeAudioEngine? engine = null,
        FakeAsioSampleRateController? asioSampleRateController = null)
        => new(controller, repository, engine ?? new FakeAudioEngine(), asioSampleRateController ?? new FakeAsioSampleRateController(), "fake-output");

    [Fact]
    public void ResolveForDevice_WithNoPersistedPreference_AppliesDefaultFormat()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { new RecordingFormat(44100, 16), RecordingFormat.Default });
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);

        viewModel.ResolveForDevice(AirDevice);

        Assert.Equal(RecordingFormat.Default, viewModel.SelectedFormat);
        Assert.Equal(RecordingFormat.Default, controller.GetCurrentFormat(AirDevice.Id));
        Assert.Null(viewModel.StatusMessage);
    }

    [Fact]
    public void SelectedFormatChanged_ToSupportedFormat_PersistsAndAppliesIt()
    {
        var controller = new FakeRecordingFormatController();
        var supported = new[] { new RecordingFormat(44100, 16), RecordingFormat.Default };
        controller.SetSupportedFormats(AirDevice.Id, supported);
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);
        viewModel.ResolveForDevice(AirDevice);

        viewModel.SelectedFormat = new RecordingFormat(44100, 16);

        Assert.Equal(new RecordingFormat(44100, 16), controller.GetCurrentFormat(AirDevice.Id));
        Assert.Equal(new RecordingFormat(44100, 16), repository.Load(AirDevice.Id));
        Assert.Null(viewModel.StatusMessage);
    }

    [Fact]
    public void SelectedFormatChanged_ToUnsupportedFormat_IsRejectedAndKeepsPreviousFormatActive()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { RecordingFormat.Default });
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);
        viewModel.ResolveForDevice(AirDevice);

        viewModel.SelectedFormat = new RecordingFormat(96000, 24);

        Assert.Equal(RecordingFormat.Default, controller.GetCurrentFormat(AirDevice.Id));
        Assert.NotNull(viewModel.StatusMessage);
    }

    [Fact]
    public void ResolveForDevice_WithPersistedPreferenceNoLongerSupported_FallsBackToDefaultWithMessage()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { RecordingFormat.Default });
        var repository = new FakeRecordingFormatRepository();
        repository.Save(AirDevice.Id, new RecordingFormat(96000, 24));
        var viewModel = CreateViewModel(controller, repository);

        viewModel.ResolveForDevice(AirDevice);

        Assert.Equal(RecordingFormat.Default, viewModel.SelectedFormat);
        Assert.Equal(RecordingFormat.Default, controller.GetCurrentFormat(AirDevice.Id));
        Assert.NotNull(viewModel.StatusMessage);
    }

    /// <summary>
    /// SyncDisplayOnly é usado depois que a captura já está rodando (MainWindowViewModel.
    /// RefreshDeviceDependentSections) — nunca pode escrever no dispositivo, mesmo quando o
    /// formato salvo diverge do atual, porque isso reconfiguraria o dispositivo com a captura
    /// ativa sem um Stop/Start ao redor, travando os meters silenciosamente (bug reportado).
    /// </summary>
    [Fact]
    public void SyncDisplayOnly_NeverWritesToDevice_EvenWithMismatchedPersistedFormat()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { new RecordingFormat(44100, 16), RecordingFormat.Default });
        controller.SetCurrentFormat(AirDevice.Id, RecordingFormat.Default);
        var repository = new FakeRecordingFormatRepository();
        repository.Save(AirDevice.Id, new RecordingFormat(44100, 16));
        var viewModel = CreateViewModel(controller, repository);

        viewModel.SyncDisplayOnly(AirDevice);

        Assert.Equal(0, controller.TrySetFormatCallCount);
        Assert.Equal(RecordingFormat.Default, viewModel.SelectedFormat);
        Assert.True(viewModel.IsAirDeviceActive);
    }

    [Fact]
    public void SyncDisplayOnly_ClearsDisplay_WhenActiveDeviceIsNotAir()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { RecordingFormat.Default });
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);
        viewModel.ResolveForDevice(AirDevice);

        viewModel.SyncDisplayOnly(OtherDevice);

        Assert.False(viewModel.IsAirDeviceActive);
        Assert.Empty(viewModel.AvailableFormats);
        Assert.Null(viewModel.SelectedFormat);
    }

    /// <summary>
    /// ResolveForDevice roda ANTES do Start — deliberadamente NÃO consulta o ASIO (abrir/fechar
    /// uma sessão ASIO bem antes do WasapiCapture.StartRecording() negociar se mostrou capaz de
    /// perturbar essa negociação neste hardware, zerando ActiveInputChannelCount e esvaziando o
    /// seletor de modo de roteamento — bug reportado). A lista completa (sem filtro por ASIO) é
    /// usada aqui mesmo quando uma leitura de ASIO estaria disponível.
    /// </summary>
    [Fact]
    public void ResolveForDevice_NeverFiltersByAsioSampleRate()
    {
        var controller = new FakeRecordingFormatController();
        var supported = new[]
        {
            new RecordingFormat(44100, 16), new RecordingFormat(44100, 24),
            new RecordingFormat(48000, 16), new RecordingFormat(48000, 24), RecordingFormat.Default,
        };
        controller.SetSupportedFormats(AirDevice.Id, supported);
        var repository = new FakeRecordingFormatRepository();
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(44100);
        var viewModel = CreateViewModel(controller, repository, asioSampleRateController: asioController);

        viewModel.ResolveForDevice(AirDevice);

        Assert.Equal(supported, viewModel.AvailableFormats);
    }

    /// <summary>
    /// REVISADO na feature 004 (S3/R3, FR-015a): SyncDisplayOnly roda com a captura ATIVA, então a
    /// consulta ao ASIO que existia aqui (<c>FilterByAsioSampleRate</c>) perturbava a negociação
    /// WASAPI e zerava os canais. A restrição do dropdown pela taxa do driver continua existindo,
    /// mas passou para <c>RefreshFormatOptionsFromDriver</c>, dentro de uma pausa de reconfiguração.
    /// </summary>
    [Fact]
    public void SyncDisplayOnly_NeverQueriesTheAsioDriverWithCaptureActive()
    {
        var controller = new FakeRecordingFormatController();
        var supported = new[]
        {
            new RecordingFormat(44100, 16), new RecordingFormat(44100, 24),
            new RecordingFormat(48000, 16), new RecordingFormat(48000, 24), RecordingFormat.Default,
        };
        controller.SetSupportedFormats(AirDevice.Id, supported);
        controller.SetCurrentFormat(AirDevice.Id, new RecordingFormat(44100, 16));
        var repository = new FakeRecordingFormatRepository();
        var asioController = new FakeAsioSampleRateController();
        asioController.SetCurrentSampleRate(44100);
        var viewModel = CreateViewModel(controller, repository, asioSampleRateController: asioController);

        viewModel.SyncDisplayOnly(AirDevice);

        Assert.Equal(0, asioController.GetCurrentSampleRateCallCount);
        Assert.Equal(supported, viewModel.AvailableFormats);
    }

    /// <summary>Sem leitura de ASIO disponível, a lista completa (sem filtro) continua sendo oferecida.</summary>
    [Fact]
    public void SyncDisplayOnly_WithNoAsioReading_DoesNotFilterAvailableFormats()
    {
        var controller = new FakeRecordingFormatController();
        var supported = new[] { new RecordingFormat(44100, 16), RecordingFormat.Default };
        controller.SetSupportedFormats(AirDevice.Id, supported);
        controller.SetCurrentFormat(AirDevice.Id, RecordingFormat.Default);
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);

        viewModel.SyncDisplayOnly(AirDevice);

        Assert.Equal(supported, viewModel.AvailableFormats);
    }

    [Fact]
    public void ResolveForDevice_HidesControls_WhenActiveDeviceIsNotAir()
    {
        var controller = new FakeRecordingFormatController();
        controller.SetSupportedFormats(AirDevice.Id, new[] { RecordingFormat.Default });
        var repository = new FakeRecordingFormatRepository();
        var viewModel = CreateViewModel(controller, repository);
        viewModel.ResolveForDevice(AirDevice);
        Assert.True(viewModel.IsAirDeviceActive);

        viewModel.ResolveForDevice(OtherDevice);

        Assert.False(viewModel.IsAirDeviceActive);
        Assert.Empty(viewModel.AvailableFormats);
        Assert.Null(viewModel.SelectedFormat);
    }
}
