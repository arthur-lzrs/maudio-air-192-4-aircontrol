using System.Windows.Controls;
using AirControl.App.ViewModels;

namespace AirControl.App.Views;

public partial class InputDeviceSelectorView : UserControl
{
    public InputDeviceSelectorView()
    {
        InitializeComponent();
    }

    public InputDeviceSelectorView(InputDeviceSelectorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
