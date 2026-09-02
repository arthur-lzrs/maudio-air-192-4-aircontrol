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

        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.TryAcquire())
        {
            Shutdown();
            return;
        }

        var settingsRepository = new SettingsRepository();
        _deviceProvider = new AudioDeviceProvider();
        _audioEngine = new AudioEngine();

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

        var mainWindowViewModel = new MainWindowViewModel(_audioEngine, _deviceProvider, settingsRepository);
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
