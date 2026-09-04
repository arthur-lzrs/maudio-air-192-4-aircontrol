using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

        Pause = new ReconfigurationPause(
            stopCapture: () => _audioEngine.Stop(),
            startCapture: RestartCapture);
    }

    /// <summary>
    /// A pausa de reconfiguração usada por esta seção. Exposta para que o
    /// <c>MainWindowViewModel</c> assine <c>PhaseChanged</c> e mostre o estado transitório
    /// "Reconfigurando…" (FR-015c) e o erro acionável de uma pausa que falhou (FR-015d).
    /// </summary>
    public ReconfigurationPause Pause { get; }

    private void RestartCapture()
    {
        if (_deviceId is not null)
        {
            _audioEngine.Start(_deviceId, _outputDeviceId);
        }
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
    /// <para>
    /// NÃO consulta o driver ASIO (corrige S3/R3): o antigo <c>FilterByAsioSampleRate</c> abria uma
    /// sessão ASIO com a captura WASAPI ATIVA, perturbando a negociação e zerando
    /// <c>ActiveInputChannelCount</c>. A consulta em tempo real passou a acontecer dentro de uma
    /// <see cref="ReconfigurationPause"/> (<see cref="RefreshFormatOptionsFromDriverCommand"/>).
    /// </para>
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
        AvailableFormats = _controller.GetSupportedFormats(deviceId);
        SetSelectedFormatWithoutApplying(_controller.GetCurrentFormat(deviceId));
    }

    /// <summary>
    /// FR-011/FR-015/FR-015a: consulta o sample rate do driver ASIO EM TEMPO REAL, mas dentro de uma
    /// pausa de reconfiguração (captura parada, teto de 2s, restabelecimento garantido) — e restringe
    /// as opções do "Formato de gravação (Windows)" às combinações que casam com essa taxa.
    /// Disparado só pelo gatilho discreto <see cref="ReconfigurationTrigger.OpenFormatList"/>
    /// (abrir a lista) — nunca por polling (FR-015b/SC-004b).
    /// </summary>
    [RelayCommand]
    public void RefreshFormatOptionsFromDriver()
    {
        if (!IsAirDeviceActive || _deviceId is null)
        {
            return;
        }

        int? driverSampleRate = null;
        var result = Pause.RunPause(
            ReconfigurationTrigger.OpenFormatList,
            () => driverSampleRate = _asioSampleRateController.GetCurrentSampleRate());

        if (!result.IsCompleted)
        {
            StatusMessage = result.FaultReason;
            return;
        }

        ApplyDriverSampleRateToOptions(driverSampleRate, ReconfigurationTrigger.OpenFormatList);
    }

    /// <summary>
    /// FR-012/FR-013: chamado quando o sample rate do driver acabou de mudar — a taxa já é conhecida,
    /// então NÃO há nova consulta ao driver (nenhuma pausa extra). Repopula as opções e, se o formato
    /// atual não casa mais com a taxa do driver, reconcilia aplicando um formato compatível dentro de
    /// uma pausa e reportando qual foi aplicado.
    /// </summary>
    public void RefreshFormatOptionsForDriverRate(int? driverSampleRate)
    {
        if (!IsAirDeviceActive || _deviceId is null)
        {
            return;
        }

        ApplyDriverSampleRateToOptions(driverSampleRate, ReconfigurationTrigger.ChangeDriverSampleRate);
    }

    private void ApplyDriverSampleRateToOptions(int? driverSampleRate, ReconfigurationTrigger trigger)
    {
        var deviceId = _deviceId!;
        var supported = _controller.GetSupportedFormats(deviceId);

        if (driverSampleRate is null)
        {
            // Taxa indeterminável: oferece a lista completa MAS explica por quê (FR-015c/FR-003) —
            // nunca uma lista silenciosamente diferente da esperada.
            AvailableFormats = supported;
            StatusMessage = "Não foi possível determinar o sample rate do driver ASIO; "
                + "mostrando todas as combinações suportadas pelo dispositivo.";
            return;
        }

        var matching = supported.Where(format => format.SampleRate == driverSampleRate).ToList();
        if (matching.Count == 0)
        {
            AvailableFormats = supported;
            StatusMessage = $"O dispositivo não expõe nenhum formato em {driverSampleRate}Hz "
                + "(taxa atual do driver); mostrando todas as combinações suportadas.";
            return;
        }

        AvailableFormats = matching;

        if (SelectedFormat is not null && SelectedFormat.SampleRate == driverSampleRate)
        {
            StatusMessage = null;
            return;
        }

        // Reconciliação (FR-013): o formato atual não segue mais o driver — aplica o equivalente na
        // taxa do driver (mesmo bit depth quando possível) e REPORTA o que foi aplicado.
        var reconciled = matching.FirstOrDefault(format => format.BitDepth == SelectedFormat?.BitDepth) ?? matching[0];
        ApplyFormat(reconciled, trigger, $"Formato de gravação ajustado para {reconciled} para acompanhar o driver ASIO.");
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
    /// travar o stream silenciosamente (sem lançar exceção). Agora o Stop→mutar→Start acontece
    /// dentro da <see cref="ReconfigurationPause"/> — o Start de restauração roda em <c>finally</c>,
    /// corrigindo S5 (uma escrita que lançasse deixava a engine parada).
    /// </summary>
    partial void OnSelectedFormatChanged(RecordingFormat? value)
    {
        if (_isApplyingExternalChange || value is null || _deviceId is null)
        {
            return;
        }

        ApplyFormat(value, ReconfigurationTrigger.OpenFormatList, successMessage: null);
    }

    private void ApplyFormat(RecordingFormat format, ReconfigurationTrigger trigger, string? successMessage)
    {
        var deviceId = _deviceId!;
        string? applyError = null;

        var result = Pause.RunPause(trigger, () =>
        {
            if (_controller.TrySetFormat(deviceId, format, out var error))
            {
                _repository.Save(deviceId, format);
                SetSelectedFormatWithoutApplying(format);
            }
            else
            {
                applyError = error;
                SetSelectedFormatWithoutApplying(_controller.GetCurrentFormat(deviceId));
            }
        });

        // Uma captura não restabelecida é o pior dos dois — tem precedência sobre o erro de escrita.
        StatusMessage = !result.IsCompleted
            ? result.FaultReason
            : applyError is not null
                ? $"Falha ao aplicar formato de gravação: {applyError}"
                : successMessage;
    }
}
