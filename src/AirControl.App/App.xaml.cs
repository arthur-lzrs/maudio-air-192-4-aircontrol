using System.Windows;
using AirControl.App.ViewModels;
using AirControl.App.Views;
using AirControl.Audio;
using AirControl.Core;

namespace AirControl.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstanceGuard;
    private AudioDeviceProvider? _deviceProvider;
    private IAudioEngine? _audioEngine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Ocorreu um erro inesperado:\n\n{args.Exception.Message}",
                "AIR Control",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.TryAcquire())
        {
            Shutdown();
            return;
        }

        // Ponto único de marshalling para a thread da UI (research.md §4 / R2). Criado ANTES do
        // provedor de dispositivos, que registra o callback COM já no próprio construtor — sem o
        // dispatcher em mãos nesse momento, a primeira notificação de dispositivo voltaria a
        // cruzar a thread sem marshalling.
        var uiDispatcher = new WpfUiDispatcher(Dispatcher);

        var settingsRepository = new SettingsRepository();
        _deviceProvider = new AudioDeviceProvider(uiDispatcher);
        _audioEngine = new AudioEngine(uiDispatcher);

        var profile = settingsRepository.Load();
        if (profile.OutputDeviceId is null)
        {
            var selectorViewModel = new OutputDeviceSelectorViewModel(_deviceProvider, settingsRepository);
            var selectorView = new OutputDeviceSelectorView(selectorViewModel);
            var result = selectorView.ShowDialog();
            if (result != true)
            {
                Shutdown();
                return;
            }
        }

        var recordingFormatController = new WindowsRecordingFormatController();
        var recordingFormatRepository = new RecordingFormatRepository();
        var asioSampleRateController = new AsioSampleRateController();
        var mainWindowViewModel = new MainWindowViewModel(
            _audioEngine,
            _deviceProvider,
            settingsRepository,
            recordingFormatController,
            recordingFormatRepository,
            asioSampleRateController);
        var mainWindow = new MainWindow(mainWindowViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_audioEngine as IDisposable)?.Dispose();
        _deviceProvider?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}
