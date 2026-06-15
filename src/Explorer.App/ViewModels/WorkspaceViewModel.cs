using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.FileSystem;
using Explorer.Core.Undo;
using Explorer.Core.Workspace;
using Explorer.Preview;

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
    private readonly IPreviewRendererRegistry _previewRegistry;
    private readonly TimeSpan? _previewDebounce;
    private PreviewCoordinator? _previewCoordinator;
    private PaneViewModel? _previewPane;   // 미리보기를 표시하는 페인
    private PaneViewModel? _driverPane;     // 선택을 따라가는(파일 목록) 페인

    [ObservableProperty]
    private bool _isQuickViewActive;

    public WorkspaceViewModel(
        Func<FileListViewModel> fileListFactory,
        IUndoService undo,
        IPreviewRendererRegistry previewRegistry,
        TimeSpan? previewDebounce = null)
    {
        ArgumentNullException.ThrowIfNull(fileListFactory);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(previewRegistry);
        _undo = undo;
        _previewRegistry = previewRegistry;
        _previewDebounce = previewDebounce;

        // 두 페인 모두 즉시 생성한다(VM 할당은 가볍다). 지연되는 것은 우측 페인의 "탐색"으로,
        // 듀얼 모드를 처음 켤 때 ActivateCurrentTabAsync가 수행한다.
        _leftPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
        _rightPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
    }

    /// <summary>반대 페인 미리보기(Ctrl+Q)가 쓰는 공유 미리보기 VM.</summary>
    public PreviewViewModel Preview { get; } = new();

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
    private void SwitchPane()
    {
        // 미리보기 페인이 활성이 되는 혼란을 막기 위해 먼저 끈다.
        DeactivateQuickView();
        ActiveSide = ActiveSide == PaneSide.Left ? PaneSide.Right : PaneSide.Left;
    }

    /// <summary>Ctrl+U — 좌/우 페인 내용 교환 (커서는 같은 쪽에 남는다, TC 동작).</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private void SwapPanes()
    {
        // 스왑은 미리보기 표면/구동 관계를 뒤집어 혼란스러우므로 먼저 끈다.
        DeactivateQuickView();

        // 프로퍼티 setter 두 번으로 바꾸면 첫 알림 시점에 좌==우인 중간 상태가 바인딩에 노출된다 —
        // 백킹 필드를 먼저 모두 교환한 뒤 일괄 통지한다.
        (_leftPane, _rightPane) = (_rightPane, _leftPane);
        OnPropertyChanged(nameof(LeftPane));
        OnPropertyChanged(nameof(RightPane));
        NotifyPaneDerivedProperties();
    }

    /// <summary>두 페인을 이름·날짜·크기로 비교해 행 색으로 표시한다(이쪽만=앰버/최신=초록/오래됨=파랑). 다시 누르면 갱신.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private void ComparePanes()
    {
        Explorer.App.Services.PaneComparer.Compare(LeftPane.FileList.Items, RightPane.FileList.Items);
    }

    /// <summary>비교 색 표시를 지운다.</summary>
    [RelayCommand]
    private void ClearComparison()
    {
        Explorer.App.Services.PaneComparer.Clear(LeftPane.FileList.Items);
        Explorer.App.Services.PaneComparer.Clear(RightPane.FileList.Items);
    }

    /// <summary>Ctrl+Q — 반대 페인 미리보기 토글. 미리보기는 비활성 페인에 뜨고 활성 페인 선택을 따라간다.</summary>
    [RelayCommand(CanExecute = nameof(IsDualMode))]
    private void ToggleQuickView()
    {
        if (IsQuickViewActive)
        {
            DeactivateQuickView();
        }
        else
        {
            ActivateQuickView();
        }
    }

    private void ActivateQuickView()
    {
        _previewCoordinator ??= CreateCoordinator();
        _previewPane = InactivePane;
        _driverPane = ActivePane;
        _previewPane.EnterPreview(Preview);
        _driverPane.FileList.PropertyChanged += OnDriverPropertyChanged;
        IsQuickViewActive = true;
        RequestPreviewForCurrentSelection();
    }

    private void DeactivateQuickView()
    {
        if (!IsQuickViewActive)
        {
            return;
        }

        if (_driverPane is not null)
        {
            _driverPane.FileList.PropertyChanged -= OnDriverPropertyChanged;
        }

        _previewPane?.ExitPreview();
        _previewPane = null;
        _driverPane = null;
        _previewCoordinator?.Clear();
        Preview.Clear();
        IsQuickViewActive = false;
    }

    private PreviewCoordinator CreateCoordinator()
    {
        var coordinator = new PreviewCoordinator(_previewRegistry, _previewDebounce);
        coordinator.LoadingChanged += (_, loading) => Preview.IsLoading = loading;
        coordinator.PreviewReady += (_, result) => Preview.Apply(result);
        return coordinator;
    }

    private void OnDriverPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileListViewModel.SelectedItem))
        {
            RequestPreviewForCurrentSelection();
        }
    }

    private void RequestPreviewForCurrentSelection()
    {
        var selected = _driverPane?.FileList.SelectedItem;
        var path = selected is { IsDirectory: false } file ? file.Entry.FullPath : null;
        _previewCoordinator?.Request(path);
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

        // 탭 이동은 페인 내용을 바꾸므로 미리보기 컨텍스트가 어긋난다 — 먼저 끈다.
        DeactivateQuickView();

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
        DeactivateQuickView(); // 구동 페인 구독 해제
        _previewCoordinator?.Dispose();
        LeftPane.Dispose();
        RightPane.Dispose();
    }

    partial void OnIsDualModeChanged(bool value)
    {
        if (!value)
        {
            // 단일 모드로 돌아가면 반대 페인 미리보기를 끈다.
            DeactivateQuickView();
        }

        SwitchPaneCommand.NotifyCanExecuteChanged();
        SwapPanesCommand.NotifyCanExecuteChanged();
        CopyToOtherPaneCommand.NotifyCanExecuteChanged();
        MoveToOtherPaneCommand.NotifyCanExecuteChanged();
        OpenInOtherPaneCommand.NotifyCanExecuteChanged();
        ToggleQuickViewCommand.NotifyCanExecuteChanged();
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
