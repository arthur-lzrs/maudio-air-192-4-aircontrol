using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AirControl.App.ViewModels;

/// <summary>
/// Expõe o "Formato Padrão" (sample rate/bit depth) do Windows para o dispositivo de gravação
/// ativo, visível/habilitado apenas quando esse dispositivo é o M-Audio (research.md §7). Segue
/// o fluxo de resolução/fallback de contracts/recording-format-contract.md.
/// </summary>
public partial class RecordingFormatSelectorViewModel : ViewModelBase
{
    private readonly IRecordingFormatController _controller;
    private readonly IRecordingFormatRepository _repository;
    private readonly IAudioEngine _audioEngine;
    private readonly IAsioSampleRateController _asioSampleRateController;
    private readonly string _outputDeviceId;
    private bool _isApplyingExternalChange;
    private string? _deviceId;

    [ObservableProperty]
    private bool _isAirDeviceActive;

    [ObservableProperty]
    private IReadOnlyList<RecordingFormat> _availableFormats = Array.Empty<RecordingFormat>();

    [ObservableProperty]
    private RecordingFormat? _selectedFormat;

    [ObservableProperty]
    private string? _statusMessage;

    public RecordingFormatSelectorViewModel(
        IRecordingFormatController controller,
        IRecordingFormatRepository repository,
        IAudioEngine audioEngine,
        IAsioSampleRateController asioSampleRateController,
        string outputDeviceId)
    {
        _controller = controller;
        _repository = repository;
        _audioEngine = audioEngine;
        _asioSampleRateController = asioSampleRateController;
        _outputDeviceId = outputDeviceId;
    }

    /// <summary>
    /// O sample rate é efetivamente governado pelo relógio do driver ASIO (o "chefe" — o Windows
    /// segue esse valor, não o contrário, ver DriverSettingsViewModel). Restringir o dropdown do
    /// Windows às combinações que já compartilham esse sample rate evita oferecer opções que só
    /// criariam uma nova dessincronia ASIO/Windows ao serem aplicadas. Sem leitura de ASIO
    /// disponível (driver ausente), cai para a lista completa sem filtro.
    /// </summary>
    /// <remarks>
    /// USADA SÓ EM <see cref="SyncDisplayOnly"/> (pós-Start) — nunca em <see cref="ResolveForDevice"/>
    /// (pré-Start). Abrir/fechar uma sessão ASIO logo antes do <c>WasapiCapture.StartRecording()</c>
    /// negociar com o mesmo dispositivo se mostrou capaz de perturbar essa negociação neste
    /// hardware (confirmado ao vivo: causou <c>ActiveInputChannelCount == 0</c>, esvaziando o
    /// seletor de modo de roteamento e derrubando os meters). Filtrar o dropdown é só uma
    /// melhoria cosmética — não vale o risco de interferir na captura.
    /// </remarks>
    private IReadOnlyList<RecordingFormat> FilterByAsioSampleRate(IReadOnlyList<RecordingFormat> formats)
    {
        var asioSampleRate = _asioSampleRateController.GetCurrentSampleRate();
        if (asioSampleRate is null)
        {
            return formats;
        }

        var filtered = formats.Where(f => f.SampleRate == asioSampleRate).ToList();
        return filtered.Count > 0 ? filtered : formats;
    }

    /// <summary>
    /// Deve ser chamado ANTES do próximo <see cref="IAudioEngine.Start"/> — via
    /// <see cref="InputDeviceSelectorViewModel.BeforeEngineStart"/> — assim como sempre que o
    /// dispositivo de entrada ativo muda (seleção manual, reconexão, startup). Se o dispositivo
    /// não for o M-Audio, os controles ficam vazios/desabilitados; caso contrário, resolve a
    /// preferência salva contra os formatos suportados, aplicando <see cref="RecordingFormat.Default"/>
    /// com aviso se ela não for mais suportada (FR-005). Nunca reinicia o engine sozinho: como é
    /// chamado antes do <c>Start</c>, a captura já nasce com o formato correto — corrigir depois
    /// de já iniciado exigiria um Stop+Start extra e arriscaria a primeira captura acontecer com
    /// o "Formato Padrão" desatualizado do Windows (ex.: 44.1kHz herdado do boot do Windows).
    /// Deliberadamente NÃO consulta o ASIO aqui (ver remarks de <see cref="FilterByAsioSampleRate"/>)
    /// — usa a lista completa de candidatos e o Default fixo (48kHz/32-bit), que não dependem de
    /// nenhuma interação com o driver ASIO antes do Start.
    /// </summary>
    public void ResolveForDevice(AudioInputDeviceInfo? device)
    {
        _deviceId = device?.Id;
        IsAirDeviceActive = device?.IsAirDevice == true;

        if (!IsAirDeviceActive || _deviceId is null)
        {
            AvailableFormats = Array.Empty<RecordingFormat>();
            SetSelectedFormatWithoutApplying(null);
            StatusMessage = null;
            return;
        }

        var deviceId = _deviceId;
        AvailableFormats = _controller.GetSupportedFormats(deviceId);

        var persisted = _repository.Load(deviceId);
        var resolved = persisted is not null && AvailableFormats.Contains(persisted)
            ? persisted
            : RecordingFormat.Default;

        StatusMessage = persisted is not null && !AvailableFormats.Contains(persisted)
            ? $"O formato salvo ({persisted.SampleRate}Hz/{persisted.BitDepth}-bit) não é mais suportado por este dispositivo; usando {RecordingFormat.Default}."
            : null;

        var current = _controller.GetCurrentFormat(deviceId);
        if (current == resolved)
        {
            SetSelectedFormatWithoutApplying(resolved);
            return;
        }

        if (_controller.TrySetFormat(deviceId, resolved, out var error))
        {
            _repository.Save(deviceId, resolved);
            SetSelectedFormatWithoutApplying(resolved);
        }
        else
        {
            StatusMessage = $"Falha ao aplicar formato de gravação: {error}";
            SetSelectedFormatWithoutApplying(current);
        }
    }

    /// <summary>
    /// Atualiza a exibição (seção visível, formatos disponíveis, formato selecionado) SEM nunca
    /// escrever no dispositivo — para ser chamada depois que a captura já está rodando (ex.:
    /// <c>MainWindowViewModel.RefreshDeviceDependentSections</c>), quando <see cref="ResolveForDevice"/>
    /// já rodou antes do <c>Start</c> via <see cref="InputDeviceSelectorViewModel.BeforeEngineStart"/>.
    /// Chamar <see cref="ResolveForDevice"/> de novo nesse ponto, com a captura já ativa, podia
    /// levar a um novo <c>TrySetFormat</c> (renegociando o dispositivo em uso) sem o Stop/Start ao
    /// redor — travava os meters silenciosamente, sem lançar exceção.
    /// </summary>
    public void SyncDisplayOnly(AudioInputDeviceInfo? device)
    {
        _deviceId = device?.Id;
        IsAirDeviceActive = device?.IsAirDevice == true;

        if (!IsAirDeviceActive || _deviceId is null)
        {
            AvailableFormats = Array.Empty<RecordingFormat>();
            SetSelectedFormatWithoutApplying(null);
            return;
        }

        var deviceId = _deviceId;
        AvailableFormats = FilterByAsioSampleRate(_controller.GetSupportedFormats(deviceId));
        SetSelectedFormatWithoutApplying(_controller.GetCurrentFormat(deviceId));
    }

    /// <summary>
    /// Registra uma falha inesperada (não tratada) ocorrida durante <see cref="ResolveForDevice"/>,
    /// chamado pelo <c>MainWindowViewModel</c> quando isola a chamada em try/catch — garante que
    /// uma exceção aqui nunca impeça outras seções (ex.: driver M-Audio) de atualizar.
    /// </summary>
    public void ReportUnexpectedFailure(Exception ex) =>
        StatusMessage = $"Falha ao resolver formato de gravação: {ex.Message}";

    private void SetSelectedFormatWithoutApplying(RecordingFormat? format)
    {
        _isApplyingExternalChange = true;
        SelectedFormat = format;
        _isApplyingExternalChange = false;
    }

    /// <summary>
    /// Para a captura ANTES de escrever o novo formato, não depois: renegociar o "Formato
    /// Padrão" do dispositivo enquanto a captura WASAPI do próprio app ainda está lendo dele pode
    /// travar o stream silenciosamente (sem lançar exceção) — mesma classe de problema corrigida
    /// em DriverSettingsViewModel.OnSelectedSampleRateChanged. Reinicia ao final, sucesso ou não.
    /// </summary>
    partial void OnSelectedFormatChanged(RecordingFormat? value)
    {
        if (_isApplyingExternalChange || value is null || _deviceId is null)
        {
            return;
        }

        var deviceId = _deviceId;
        _audioEngine.Stop();

        if (_controller.TrySetFormat(deviceId, value, out var error))
        {
            _repository.Save(deviceId, value);
            StatusMessage = null;
        }
        else
        {
            StatusMessage = $"Falha ao aplicar formato de gravação: {error}";
            SetSelectedFormatWithoutApplying(_controller.GetCurrentFormat(deviceId));
        }

        _audioEngine.Start(deviceId, _outputDeviceId);
    }
}
