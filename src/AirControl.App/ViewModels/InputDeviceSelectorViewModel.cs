using AirControl.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AirControl.App.ViewModels;

public partial class InputDeviceSelectorViewModel : ViewModelBase
{
    private readonly IAudioDeviceProvider _deviceProvider;
    private readonly IAudioEngine _audioEngine;
    private readonly ISettingsRepository _settingsRepository;
    private readonly string _outputDeviceId;
    private bool _isApplyingExternalChange;

    [ObservableProperty]
    private IReadOnlyList<AudioInputDeviceInfo> _availableDevices = Array.Empty<AudioInputDeviceInfo>();

    [ObservableProperty]
    private AudioInputDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private bool _needsSelection;

    public event EventHandler? ActiveDeviceChanged;

    /// <summary>
    /// Disparado depois do <see cref="IAudioEngine.Stop"/> e antes do próximo
    /// <see cref="IAudioEngine.Start"/>, para que assinantes (ex.: correção do "Formato Padrão"
    /// do Windows) apliquem ajustes no dispositivo antes da captura ser (re)aberta — corrigir o
    /// formato só depois de já iniciado exigiria um Stop+Start extra e arriscaria a primeira
    /// captura acontecer com o formato errado do Windows (bug reportado: app abre mas não
    /// funciona quando o Windows inicia com 44.1kHz).
    /// </summary>
    public event EventHandler<AudioInputDeviceInfo>? BeforeEngineStart;

    /// <summary>
    /// Última falha do <see cref="IAudioEngine.Start"/> em <see cref="StartWith"/>, ou null se o
    /// último Start teve sucesso. O dispositivo continua "selecionado" (<see cref="SelectedDevice"/>)
    /// mesmo quando a captura falha (ex.: formato não suportado, 0x88890008) — isolar a falha
    /// aqui evita que ela impeça as demais seções dependentes do dispositivo (formato de
    /// gravação, driver) de atualizar (mesma classe de bug corrigida em
    /// MainWindowViewModel.RefreshDeviceDependentSections: uma exceção não isolada bloqueava
    /// outras seções mesmo com o dispositivo reconhecido).
    /// </summary>
    public Exception? StartFailure { get; private set; }

    public InputDeviceSelectorViewModel(
        IAudioDeviceProvider deviceProvider,
        IAudioEngine audioEngine,
        ISettingsRepository settingsRepository,
        string outputDeviceId)
    {
        _deviceProvider = deviceProvider;
        _audioEngine = audioEngine;
        _settingsRepository = settingsRepository;
        _outputDeviceId = outputDeviceId;
    }

    /// <summary>
    /// Resolve o dispositivo de entrada ativo a partir da preferência persistida (FR-011): usa a
    /// seleção manual salva se o dispositivo ainda estiver conectado; senão, auto-detecta o AIR
    /// (FR-008); se nada resolver, expõe um estado de "precisa selecionar" (FR-009) sem iniciar o
    /// engine. Não escreve <c>InputDeviceId</c> na auto-detecção (data-model.md).
    /// </summary>
    public void ResolveActiveDevice()
    {
        RefreshAvailableDevices();

        var profile = _settingsRepository.Load();
        var manualDevice = profile.InputDeviceId is not null
            ? AvailableDevices.FirstOrDefault(d => d.Id == profile.InputDeviceId)
            : null;

        if (manualDevice is not null)
        {
            StartWith(manualDevice);
            return;
        }

        var autoDevice = AvailableDevices.FirstOrDefault(d => d.IsAirDevice);
        if (autoDevice is not null)
        {
            StartWith(autoDevice);
            return;
        }

        NeedsSelection = true;
        _audioEngine.Stop();
    }

    /// <summary>
    /// Só lista dispositivos M-Audio — este app é específico para o AIR 192|4, não um seletor de
    /// entrada genérico (research.md, escopo do produto). Um dispositivo não-M-Audio salvo de uma
    /// versão anterior deixa de ser encontrado aqui e cai automaticamente para a auto-detecção do
    /// AIR em <see cref="ResolveActiveDevice"/>.
    /// </summary>
    public void RefreshAvailableDevices() =>
        AvailableDevices = _deviceProvider.GetAvailableInputDevices().Where(d => d.IsAirDevice).ToList();

    /// <summary>
    /// Força uma reconexão completa com o dispositivo M-Audio ativo — o mesmo efeito que trocar
    /// para outro dispositivo e voltar, só que sem precisar de um segundo dispositivo disponível
    /// para "resetar" a aplicação.
    /// </summary>
    [RelayCommand]
    public void RestartConnection()
    {
        _audioEngine.Stop();
        ResolveActiveDevice();
    }

    private void StartWith(AudioInputDeviceInfo device)
    {
        NeedsSelection = false;
        _isApplyingExternalChange = true;
        SelectedDevice = device;
        _isApplyingExternalChange = false;

        _audioEngine.Stop();
        BeforeEngineStart?.Invoke(this, device);

        try
        {
            _audioEngine.Start(device.Id, _outputDeviceId);
            StartFailure = null;
        }
        catch (Exception ex)
        {
            StartFailure = ex;
        }
    }

    partial void OnSelectedDeviceChanged(AudioInputDeviceInfo? value)
    {
        if (_isApplyingExternalChange || value is null)
        {
            return;
        }

        StartWith(value);

        var profile = _settingsRepository.Load();
        _settingsRepository.Save(profile with { InputDeviceId = value.Id });

        ActiveDeviceChanged?.Invoke(this, EventArgs.Empty);
    }
}
