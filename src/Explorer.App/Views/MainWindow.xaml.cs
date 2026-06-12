using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Explorer.App.Input;
using Explorer.App.ViewModels;
using Explorer.Core.Input;
using Explorer.Core.Workspace;
using Wpf.Ui.Controls;

namespace Explorer.App.Views;

public partial class MainWindow : FluentWindow
{
    /// <summary>키맵 액션 → MainWindowViewModel 기준 명령 바인딩 경로.</summary>
    private static readonly IReadOnlyDictionary<string, string> ActionCommandPaths = new Dictionary<string, string>
    {
        [KeyActions.GoBack] = "Workspace.ActiveFileList.GoBackCommand",
        [KeyActions.GoForward] = "Workspace.ActiveFileList.GoForwardCommand",
        [KeyActions.GoUp] = "Workspace.ActiveFileList.GoUpCommand",
        [KeyActions.Refresh] = "Workspace.ActiveFileList.RefreshCommand",
        [KeyActions.NewTab] = "Workspace.ActivePane.AddTabCommand",
        [KeyActions.CloseTab] = "Workspace.ActivePane.CloseTabCommand",
        [KeyActions.NextTab] = "Workspace.ActivePane.NextTabCommand",
        [KeyActions.ToggleDualMode] = "Workspace.ToggleDualModeCommand",
        [KeyActions.SwitchPane] = "Workspace.SwitchPaneCommand",
        [KeyActions.SwapPanes] = "Workspace.SwapPanesCommand",
        [KeyActions.CopyToOtherPane] = "Workspace.CopyToOtherPaneCommand",
        [KeyActions.MoveToOtherPane] = "Workspace.MoveToOtherPaneCommand",
        [KeyActions.OpenInOtherPane] = "Workspace.OpenInOtherPaneCommand",
        [KeyActions.Undo] = "Workspace.UndoCommand",
    };

    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ApplyKeyBindings();

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

    /// <summary>중앙 키맵(keymap.json 오버라이드 포함)으로 윈도우 키 바인딩을 구성한다.</summary>
    private void ApplyKeyBindings()
    {
        foreach (var (action, commandPath) in ActionCommandPaths)
        {
            var gesture = _viewModel.KeyMap.GestureFor(action);
            var parsed = GestureParser.Parse(gesture);
            if (parsed.Count == 0 && !string.IsNullOrWhiteSpace(gesture))
            {
                Serilog.Log.Warning("키맵 제스처를 해석할 수 없어 '{Action}' 바인딩을 건너뜁니다: {Gesture}", action, gesture);
            }

            foreach (var (modifiers, key) in parsed)
            {
                var binding = new KeyBinding { Key = key, Modifiers = modifiers };
                BindingOperations.SetBinding(binding, InputBinding.CommandProperty, new Binding(commandPath)
                {
                    Source = _viewModel,
                });
                InputBindings.Add(binding);
            }
        }
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkspaceViewModel.IsDualMode):
                UpdatePaneLayout();
                break;
            case nameof(WorkspaceViewModel.ActiveSide):
                FocusActivePaneIfOutside();
                break;
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

    /// <summary>키보드(Tab)로 활성 페인이 바뀌면 포커스도 따라간다. 클릭으로 바뀐 경우엔 건드리지 않는다.</summary>
    private void FocusActivePaneIfOutside()
    {
        var paneView = _viewModel.Workspace.ActiveSide == PaneSide.Left ? LeftPaneView : RightPaneView;
        if (!paneView.IsKeyboardFocusWithin)
        {
            paneView.FocusFileList();
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

    private void OnFavoriteSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FavoritesList.SelectedItem is FavoriteItemViewModel favorite)
        {
            _viewModel.Favorites.OpenCommand.Execute(favorite);

            // 선택을 비워 같은 항목 재클릭도 다시 동작하게 한다.
            FavoritesList.SelectedItem = null;
        }
    }

    private void OnFavoritesDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFavoritesDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _viewModel.Favorites.AddPaths(paths);
        }
    }
}
