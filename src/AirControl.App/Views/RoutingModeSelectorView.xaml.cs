using System.Windows.Controls;
using AirControl.App.ViewModels;

namespace AirControl.App.Views;

public partial class RoutingModeSelectorView : UserControl
{
    public RoutingModeSelectorView()
    {
        InitializeComponent();
    }

    public RoutingModeSelectorView(RoutingModeSelectorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
