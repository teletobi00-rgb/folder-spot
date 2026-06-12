using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.Workspace;

namespace Explorer.App.ViewModels;

/// <summary>
/// 워크스페이스 = 좌/우 페인 + 활성 페인 추적 + 단일/듀얼 모드. 페인이 탭을 소유한다.
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

    public WorkspaceViewModel(Func<FileListViewModel> fileListFactory)
    {
        ArgumentNullException.ThrowIfNull(fileListFactory);

        // 두 페인 모두 즉시 생성한다(VM 할당은 가볍다). 지연되는 것은 우측 페인의 "탐색"으로,
        // 듀얼 모드를 처음 켤 때 ActivateCurrentTabAsync가 수행한다.
        LeftPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
        RightPane = new PaneViewModel(fileListFactory(), DefaultStartPath);
    }

    public PaneViewModel LeftPane { get; }

    public PaneViewModel RightPane { get; }

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
            IsDualMode = false;
            if (ActiveSide == PaneSide.Right)
            {
                ActiveSide = PaneSide.Left;
            }

            return;
        }

        IsDualMode = true;

        // 오른쪽 페인이 아직 탐색 전이면 활성 탭 경로로 로드한다.
        if (RightPane.FileList.CurrentPath is null)
        {
            await RightPane.ActivateCurrentTabAsync();
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
}
