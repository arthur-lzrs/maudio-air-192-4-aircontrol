using System.Windows.Controls;
using AirControl.App.ViewModels;

namespace AirControl.App.Views;

public partial class RecordingFormatSelectorView : UserControl
{
    public RecordingFormatSelectorView()
    {
        InitializeComponent();
    }

    public RecordingFormatSelectorView(RecordingFormatSelectorViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
