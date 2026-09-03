using System.Windows.Controls;
using AirControl.App.ViewModels;

namespace AirControl.App.Views;

public partial class DriverSettingsView : UserControl
{
    public DriverSettingsView()
    {
        InitializeComponent();
    }

    public DriverSettingsView(DriverSettingsViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
