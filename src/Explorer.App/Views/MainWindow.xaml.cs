using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Explorer.App.Input;
using Explorer.App.Services;
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
        [KeyActions.ToggleQuickView] = "Workspace.ToggleQuickViewCommand",
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly AppLifecycle _lifecycle;

    public MainWindow(MainWindowViewModel viewModel, AppLifecycle lifecycle)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(lifecycle);
        _viewModel = viewModel;
        _lifecycle = lifecycle;
        DataContext = viewModel;
        InitializeComponent();
        ApplyKeyBindings();

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            UpdatePaneLayout();
        };
        Closing += (_, e) =>
        {
            _viewModel.SaveSession();

            // 트레이 상주: 닫기 = 숨김 (핫키/인덱싱 유지). 진짜 종료는 트레이 메뉴에서.
            if (!_lifecycle.ExitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }

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

        // 마우스 뒤로/앞으로 버튼(XButton1/2) → 활성 페인 폴더 이동. 터널링이라 자식이 먹어도 잡는다.
        PreviewMouseDown += OnNavigationMouseButton;
    }

    /// <summary>마우스 보조 버튼으로 폴더 히스토리 뒤로/앞으로 이동(Alt+←/→와 동일 명령).</summary>
    private void OnNavigationMouseButton(object sender, MouseButtonEventArgs e)
    {
        ICommand? command = e.ChangedButton switch
        {
            MouseButton.XButton1 => _viewModel.Workspace.ActiveFileList.GoBackCommand,
            MouseButton.XButton2 => _viewModel.Workspace.ActiveFileList.GoForwardCommand,
            _ => null,
        };

        if (command is null)
        {
            return;
        }

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>트레이/검색 팝업에서 메인 창을 다시 띄울 때.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
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
