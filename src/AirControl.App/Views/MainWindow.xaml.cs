using System.Windows;
using System.Windows.Interop;
using AirControl.App.ViewModels;
using AirControl.Audio;

namespace AirControl.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Input1TrimSlider.PreviewMouseUp += (_, _) => viewModel.Input1Trim.CommitTrim();
        Input1TrimSlider.KeyUp += (_, _) => viewModel.Input1Trim.CommitTrim();
        Input2TrimSlider.PreviewMouseUp += (_, _) => viewModel.Input2Trim.CommitTrim();
        Input2TrimSlider.KeyUp += (_, _) => viewModel.Input2Trim.CommitTrim();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == unchecked((int)SingleInstanceGuard.ShowExistingInstanceMessage))
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
            handled = true;
        }

        return IntPtr.Zero;
    }
}
