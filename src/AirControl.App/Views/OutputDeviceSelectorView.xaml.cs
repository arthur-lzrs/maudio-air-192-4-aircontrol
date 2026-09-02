using System.Windows;
using AirControl.App.ViewModels;

namespace AirControl.App.Views;

public partial class OutputDeviceSelectorView : Window
{
    public OutputDeviceSelectorView(OutputDeviceSelectorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.DeviceConfirmed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }
}
