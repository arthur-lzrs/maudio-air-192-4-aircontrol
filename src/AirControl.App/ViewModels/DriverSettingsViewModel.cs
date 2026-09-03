using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirControl.App.ViewModels;

/// <summary>
/// Seção "Driver M-Audio": diagnóstico (a partir de <see cref="IAudioEngine.CaptureFormatDescription"/>),
/// controle direto do sample rate do driver ASIO (research.md §6, revisado — viável via
/// <see cref="IAsioSampleRateController"/>, sem SDK da Steinberg), e um atalho para abrir o
/// painel de controle do fabricante para o que o protocolo ASIO não expõe de forma padronizada
/// (buffer size — FR-008 permanece fora de escopo só para essa parte; FR-009 é o atalho).
/// </summary>
public partial class DriverSettingsViewModel : ViewModelBase
{
    /// <summary>
    /// Caminho real do instalador oficial M-Audio (confirmado por inspeção direta da instalação):
    /// a pasta é "AIR 192 4" (sem "|") e o executável é "Panel.exe", não "AIR Control Panel.exe"
    /// dentro de uma pasta "AIR Control Panel" — nomes que nunca existiram, causando o bug de
    /// "painel não encontrado" mesmo com o driver instalado.
    /// </summary>
    private static readonly string[] CandidatePanelPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "M-Audio", "AIR 192 4", "Panel.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "M-Audio", "AIR 192 4", "Panel.exe"),
    };

    private readonly IAudioEngine _audioEngine;
    private readonly IAsioSampleRateController _asioSampleRateController;
    private readonly string _outputDeviceId;
    private readonly Func<string?> _resolvePanelPath;
    private bool _isApplyingExternalChange;
    private string? _deviceId;

    [ObservableProperty]
    private bool _isAirDeviceActive;

    [ObservableProperty]
    private string? _diagnosticInfo;

    [ObservableProperty]
    private IReadOnlyList<int> _availableSampleRates = Array.Empty<int>();

    [ObservableProperty]
    private int? _selectedSampleRate;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _sampleRateMismatchWarning;

    public DriverSettingsViewModel(
        IAudioEngine audioEngine,
        IAsioSampleRateController asioSampleRateController,
        string outputDeviceId,
        Func<string?>? resolvePanelPath = null)
    {
        _audioEngine = audioEngine;
        _asioSampleRateController = asioSampleRateController;
        _outputDeviceId = outputDeviceId;
        _resolvePanelPath = resolvePanelPath ?? (() => CandidatePanelPaths.FirstOrDefault(File.Exists));

        Pause = new ReconfigurationPause(
            stopCapture: () => _audioEngine.Stop(),
            startCapture: RestartCapture);
    }

    /// <summary>
    /// A pausa de reconfiguração desta seção — exposta para que o <c>MainWindowViewModel</c> exiba o
    /// estado transitório "Reconfigurando…" (FR-015c) e o erro de uma pausa que falhou (FR-015d).
    /// </summary>
    public ReconfigurationPause Pause { get; }

    private void RestartCapture()
    {
        if (_deviceId is not null)
        {
            _audioEngine.Start(_deviceId, _outputDeviceId);
        }
    }

    /// <summary>Deve ser chamado sempre que o dispositivo de entrada ativo muda, mesma regra de visibilidade de <see cref="RecordingFormatSelectorViewModel"/>.</summary>
    public void UpdateForDevice(AudioInputDeviceInfo? device)
    {
        _deviceId = device?.Id;
        IsAirDeviceActive = device?.IsAirDevice == true;
        DiagnosticInfo = IsAirDeviceActive ? _audioEngine.CaptureFormatDescription : null;

        if (!IsAirDeviceActive)
        {
            AvailableSampleRates = Array.Empty<int>();
            SetSelectedSampleRateWithoutApplying(null);
            return;
        }

        AvailableSampleRates = _asioSampleRateController.GetSupportedSampleRates();
        SetSelectedSampleRateWithoutApplying(_asioSampleRateController.GetCurrentSampleRate());
    }

    /// <summary>
    /// O driver ASIO e o "Formato Padrão" que o Windows usa para captura (WASAPI) são estados
    /// independentes neste hardware — confirmado ao vivo: nem escrever a propriedade do Windows
    /// nem reconectar o dispositivo fisicamente resincroniza um com o outro. Quando divergem, o
    /// áudio capturado pelo AirControl (e por qualquer app WASAPI, incluindo chamadas de voz)
    /// usa o sample rate do Windows, não o do painel M-Audio/ASIO — descasamento que distorce o
    /// áudio (tom grave/estranho). Só o Painel de Som do Windows corrige isso de forma confiável.
    /// </summary>
    public void UpdateSampleRateMismatch(int? windowsSampleRate)
    {
        var asioSampleRate = SelectedSampleRate;
        SampleRateMismatchWarning = IsAirDeviceActive
            && asioSampleRate is not null
            && windowsSampleRate is not null
            && asioSampleRate != windowsSampleRate
                ? $"Driver ASIO em {asioSampleRate}Hz, mas o Windows está capturando em {windowsSampleRate}Hz — isso distorce o áudio (voz grave/estranha em chamadas). Ajuste manualmente pelo Windows."
                : null;
    }

    [RelayCommand]
    private void OpenWindowsSoundSettings()
    {
        try
        {
            // Abre direto na aba "Gravação" do Painel de Som clássico do Windows — o dispositivo
            // M-Audio já aparece ali; falta só Propriedades > Avançado para o formato.
            Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusMessage = $"Falha ao abrir as configurações de som do Windows: {ex.Message}";
        }
    }

    private void SetSelectedSampleRateWithoutApplying(int? sampleRate)
    {
        _isApplyingExternalChange = true;
        SelectedSampleRate = sampleRate;
        _isApplyingExternalChange = false;
    }

    /// <summary>
    /// Para a captura ANTES do handshake ASIO, não depois: abrir/fechar uma sessão ASIO enquanto
    /// a captura WASAPI do próprio app ainda está lendo o mesmo dispositivo pode travar o stream
    /// silenciosamente (sem lançar exceção) — confirmado como a causa mais provável de "os meters
    /// pararam de se mover" depois de trocar o sample rate. Reinicia ao final, sucesso ou não, já
    /// que o dispositivo pode ter sido reconfigurado de qualquer forma.
    /// </summary>
    partial void OnSelectedSampleRateChanged(int? value)
    {
        if (_isApplyingExternalChange || value is null || _deviceId is null)
        {
            return;
        }

        string? applyError = null;

        // Stop→mutar→Start dentro da pausa de reconfiguração: o Start de restauração roda em
        // finally (corrige S5, em que um handshake que lançasse deixava a engine parada), com teto
        // de 2s e estado transitório visível (FR-015a/c/d).
        var result = Pause.RunPause(ReconfigurationTrigger.ChangeDriverSampleRate, () =>
        {
            if (!_asioSampleRateController.TrySetSampleRate(value.Value, out var error))
            {
                applyError = error;
                SetSelectedSampleRateWithoutApplying(_asioSampleRateController.GetCurrentSampleRate());
            }
        });

        StatusMessage = !result.IsCompleted
            ? result.FaultReason
            : applyError is not null
                ? $"Falha ao aplicar sample rate no driver: {applyError}"
                : null;
    }

    [RelayCommand]
    private void OpenManufacturerPanel()
    {
        var path = _resolvePanelPath();
        if (path is null)
        {
            StatusMessage = "Painel M-Audio não encontrado. Verifique se o driver AIR 192|4 está instalado.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusMessage = $"Falha ao abrir o painel M-Audio: {ex.Message}";
        }
    }
}
