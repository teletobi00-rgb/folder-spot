using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Explorer.App.ViewModels;
using Explorer.Core.Workspace;
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

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            UpdatePaneLayout();
        };
        Closing += (_, _) =>
        {
            _viewModel.SaveSession();
            _viewModel.Workspace.Dispose();
        };

        _viewModel.Workspace.PropertyChanged += OnWorkspacePropertyChanged;

        // 페인 내부로 키보드 포커스가 들어오면 그 페인이 활성 페인이 된다.
        LeftPaneView.AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(
            (_, _) => _viewModel.Workspace.SetActiveSide(PaneSide.Left)), handledEventsToo: true);
        RightPaneView.AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(
            (_, _) => _viewModel.Workspace.SetActiveSide(PaneSide.Right)), handledEventsToo: true);

        LeftPaneView.CrossPaneTabDropRequested += OnCrossPaneTabDrop;
        RightPaneView.CrossPaneTabDropRequested += OnCrossPaneTabDrop;
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.IsDualMode))
        {
            UpdatePaneLayout();
        }
    }

    private void UpdatePaneLayout()
    {
        if (_viewModel.Workspace.IsDualMode)
        {
            PaneSplitterColumn.Width = new GridLength(4);
            RightPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            PaneSplitter.Visibility = Visibility.Visible;
            RightPaneView.Visibility = Visibility.Visible;
        }
        else
        {
            PaneSplitterColumn.Width = new GridLength(0);
            RightPaneColumn.Width = new GridLength(0);
            PaneSplitter.Visibility = Visibility.Collapsed;
            RightPaneView.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCrossPaneTabDrop(object? sender, CrossPaneTabDropEventArgs e)
    {
        _ = _viewModel.Workspace.MoveTabToOtherPaneAsync(e.SourcePane, e.Tab, e.InsertIndex);
    }

    private void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveList.SelectedItem is DriveItemViewModel drive)
        {
            _viewModel.DriveSidebar.OpenDriveCommand.Execute(drive);
        }
    }
}
