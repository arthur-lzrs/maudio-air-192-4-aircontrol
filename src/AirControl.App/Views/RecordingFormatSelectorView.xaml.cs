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

    /// <summary>
    /// Gatilho discreto <c>OpenFormatList</c> (FR-015b): a única forma de disparar a consulta em
    /// tempo real ao sample rate do driver. Não existe caminho periódico equivalente (SC-004b).
    /// </summary>
    private void OnFormatListDropDownOpened(object sender, EventArgs e) =>
        (DataContext as RecordingFormatSelectorViewModel)?.RefreshFormatOptionsFromDriverCommand.Execute(null);
}
