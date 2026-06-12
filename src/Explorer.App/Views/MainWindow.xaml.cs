using System.Windows.Controls;
using Explorer.App.ViewModels;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveList.SelectedItem is DriveItemViewModel drive)
        {
            _viewModel.DriveSidebar.OpenDriveCommand.Execute(drive);
        }
    }
}
