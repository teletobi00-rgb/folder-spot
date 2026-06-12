using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Undo;
using Explorer.Core.Workspace;

namespace Explorer.App.ViewModels;

/// <summary>
/// 워크스페이스 = 좌/우 페인 + 활성 페인 추적 + 단일/듀얼 모드 + 페인 간 작업(TC 키보드 계약).
/// 페인이 탭을 소유한다.
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    public static readonly string DefaultStartPath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePane))]
    [NotifyPropertyChangedFor(nameof(ActiveFileList))]
    [NotifyPropertyChangedFor(nameof(InactivePane))]
    private PaneSide _activeSide = PaneSide.Left;

    [ObservableProperty]
    private bool _isDualMode;

    // Ctrl+U 스왑이 두 참조를 "동시에" 교환해야 하므로(중간 상태 노출 금지) 수동 프로퍼티로 구현한다.
    private PaneViewModel _leftPane;
    private PaneViewModel _rightPane;

    private readonly IUndoService _undo;

    public WorkspaceViewModel(Func<FileListViewModel> fileListFactory, IUndoService undo)
    {
        ArgumentNullException.ThrowIfNull(fileListFactory);
        ArgumentNullException.ThrowIfNull(undo);
        _undo = undo;

        // 두 페인 모두 즉시 생성한다(VM 할당은 가볍다). 지연되는 것은 우측 페인의 "탐색"으로,
        // 듀얼 모드를 처음 켤 때 ActivateCurrentTabAsync가 수행한다.
        _leftPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
        _rightPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
    }

    public PaneViewModel LeftPane
    {
        get => _leftPane;
        private set
        {
            if (SetProperty(ref _leftPane, value))
            {
                NotifyPaneDerivedProperties();
            }
        }
    }

    public PaneViewModel RightPane
    {
        get => _rightPane;
        private set
        {
            if (SetProperty(ref _rightPane, value))
            {
                NotifyPaneDerivedProperties();
            }
        }
    }

    public PaneViewModel ActivePane => ActiveSide == PaneSide.Left ? LeftPane : RightPane;

    public PaneViewModel InactivePane => ActiveSide == PaneSide.Left ? RightPane : LeftPane;

    public FileListViewModel ActiveFileList => ActivePane.FileList;

    /// <summary>뷰의 포커스 이동으로 활성 페인이 바뀔 때 호출.</summary>
    public void SetActiveSide(PaneSide side)
    {
        if (side == PaneSide.Right && !IsDualMode)
        {
            return;
        }

        ActiveSide = side;
    }

    [RelayCommand]
    private async Task ToggleDualModeAsync()
    {
        if (IsDualMode)
        {
            // 듀얼이 아직 켜진 상태에서 활성 페인을 먼저 좌측으로 — 역순이면 SetActiveSide 가드와
            // 레이아웃 갱신 사이의 순서 의존이 생긴다.
            if (ActiveSide == PaneSide.Right)
            {
                ActiveSide = PaneSide.Left;
            }

            IsDualMode = false;
            return;
        }

        IsDualMode = true;

        // 오른쪽 페인이 아직 탐색 전이면 활성 탭 경로로 로드한다.
        if (RightPane.FileList.CurrentPath is null)
        {
            await RightPane.ActivateCurrentTabAsync();
        }
    }

    /// <summary>Tab — 활성 페인 전환.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private void SwitchPane() =>
        ActiveSide = ActiveSide == PaneSide.Left ? PaneSide.Right : PaneSide.Left;

    /// <summary>Ctrl+U — 좌/우 페인 내용 교환 (커서는 같은 쪽에 남는다, TC 동작).</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private void SwapPanes()
    {
        // 프로퍼티 setter 두 번으로 바꾸면 첫 알림 시점에 좌==우인 중간 상태가 바인딩에 노출된다 —
        // 백킹 필드를 먼저 모두 교환한 뒤 일괄 통지한다.
        (_leftPane, _rightPane) = (_rightPane, _leftPane);
        OnPropertyChanged(nameof(LeftPane));
        OnPropertyChanged(nameof(RightPane));
        NotifyPaneDerivedProperties();
    }

    private void NotifyPaneDerivedProperties()
    {
        OnPropertyChanged(nameof(ActivePane));
        OnPropertyChanged(nameof(InactivePane));
        OnPropertyChanged(nameof(ActiveFileList));
    }

    /// <summary>F5 — 활성 페인 선택 항목을 반대 페인 폴더로 복사.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private Task CopyToOtherPaneAsync() => TransferToOtherPaneAsync(move: false);

    /// <summary>F6 — 활성 페인 선택 항목을 반대 페인 폴더로 이동.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private Task MoveToOtherPaneAsync() => TransferToOtherPaneAsync(move: true);

    /// <summary>Ctrl+←/→ — 선택한 폴더(파일이면 현재 폴더)를 반대 페인에서 연다.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private async Task OpenInOtherPaneAsync()
    {
        var source = ActiveFileList;
        var path = source.SelectedItem is { IsDirectory: true } folder
            ? folder.Entry.FullPath
            : source.CurrentPath;

        if (path is not null)
        {
            await InactivePane.FileList.NavigateToAsync(path);
        }
    }

    /// <summary>Ctrl+Z — 마지막 파일 작업 되돌리기.</summary>
    [RelayCommand]
    private async Task UndoAsync()
    {
        if (await _undo.TryUndoAsync() is not { } undone)
        {
            ActiveFileList.StatusMessage = "되돌릴 작업이 없습니다.";
            return;
        }

        ActiveFileList.StatusMessage = undone.Result.Succeeded
            ? $"되돌렸습니다: {undone.Description}"
            : $"되돌리기 실패 ({undone.Description}): {undone.Result.Message ?? "오류"}";

        await ActiveFileList.RefreshCommand.ExecuteAsync(null);
        if (IsDualMode)
        {
            await InactivePane.FileList.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary>활성 페인의 활성 탭을 반대 페인으로 보낸다 (마지막 탭이면 무시).</summary>
    [RelayCommand]
    private async Task MoveActiveTabToOtherPaneAsync()
    {
        if (!IsDualMode || ActivePane.ActiveTab is not { } tab)
        {
            return;
        }

        await MoveTabToOtherPaneAsync(ActivePane, tab);
    }

    public async Task MoveTabToOtherPaneAsync(PaneViewModel sourcePane, TabViewModel tab, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePane);
        ArgumentNullException.ThrowIfNull(tab);

        var targetPane = ReferenceEquals(sourcePane, LeftPane) ? RightPane : LeftPane;
        if (await sourcePane.DetachTabAsync(tab) is not { } detached)
        {
            return;
        }

        await targetPane.ReceiveTabAsync(detached, insertIndex);
    }

    public WorkspaceSession CaptureSession() => new(
        LeftPane.CaptureSession(),
        RightPane.CaptureSession(),
        IsDualMode,
        ActiveSide);

    public async Task RestoreSessionAsync(WorkspaceSession? session)
    {
        if (session is not null)
        {
            LeftPane.RestoreSession(session.Left);
            RightPane.RestoreSession(session.Right);
            IsDualMode = session.IsDualMode;
            ActiveSide = session.IsDualMode ? session.ActiveSide : PaneSide.Left;
        }

        // 보이는 페인만 즉시 탐색한다 — 비활성 탭은 활성화 시점에 로드(지연).
        await LeftPane.ActivateCurrentTabAsync();
        if (IsDualMode)
        {
            await RightPane.ActivateCurrentTabAsync();
        }
    }

    public void Dispose()
    {
        LeftPane.Dispose();
        RightPane.Dispose();
    }

    partial void OnIsDualModeChanged(bool value)
    {
        SwitchPaneCommand.NotifyCanExecuteChanged();
        SwapPanesCommand.NotifyCanExecuteChanged();
        CopyToOtherPaneCommand.NotifyCanExecuteChanged();
        MoveToOtherPaneCommand.NotifyCanExecuteChanged();
        OpenInOtherPaneCommand.NotifyCanExecuteChanged();
    }

    private async Task TransferToOtherPaneAsync(bool move)
    {
        var source = ActiveFileList;
        var target = InactivePane.FileList;
        var destination = target.CurrentPath;
        var selection = source.SelectedItems;
        if (selection.Count == 0 || destination is null)
        {
            return;
        }

        if (source.CurrentPath is { } sourceDir
            && string.Equals(PathUtils.Normalize(sourceDir), PathUtils.Normalize(destination), StringComparison.OrdinalIgnoreCase))
        {
            source.StatusMessage = "두 페인이 같은 폴더를 보고 있습니다.";
            return;
        }

        var paths = selection.Select(i => i.Entry.FullPath).ToArray();
        var operation = move ? DropOperation.Move : DropOperation.Copy;
        if (!DropRules.CanDrop(paths, destination, operation))
        {
            source.StatusMessage = "이 위치로는 보낼 수 없습니다.";
            return;
        }

        await target.HandleDropAsync(paths, destination, operation);
    }
}
