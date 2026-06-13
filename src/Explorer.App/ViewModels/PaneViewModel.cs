using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.Core.Workspace;

namespace Explorer.App.ViewModels;

/// <summary>
/// 페인 하나 = 탭 스트립 + 파일 목록. 탭은 경량 TabState로만 보유하고
/// FileListViewModel 인스턴스는 페인당 1개를 활성 탭 전환 시 재사용한다(R-TABMEM).
/// </summary>
public sealed partial class PaneViewModel : ObservableObject, IDisposable
{
    private PaneState _state;
    private bool _syncingTabs;
    private bool _switchingTab;
    private int _switchVersion;

    [ObservableProperty]
    private IReadOnlyList<TabViewModel> _tabs = [];

    [ObservableProperty]
    private TabViewModel? _activeTab;

    /// <summary>이 페인이 미리보기 표면일 때 true — 뷰는 파일 목록 대신 미리보기를 보여준다.</summary>
    [ObservableProperty]
    private bool _isPreviewMode;

    /// <summary>미리보기 표면일 때 바인딩할 공유 미리보기 VM (워크스페이스 소유).</summary>
    [ObservableProperty]
    private PreviewViewModel? _preview;

    public PaneViewModel(FileListViewModel fileList, string initialPath)
    {
        ArgumentNullException.ThrowIfNull(fileList);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialPath);
        FileList = fileList;
        _state = PaneState.Create(TabState.Create(initialPath));
        FileList.PropertyChanged += OnFileListPropertyChanged;
        RebuildTabs();
    }

    public FileListViewModel FileList { get; }

    public int TabCount => _state.Tabs.Length;

    /// <summary>활성 탭의 경로로 첫 탐색을 수행한다 (세션 복원/초기화 시 호출).</summary>
    public Task ActivateCurrentTabAsync() => ApplyActiveTabToFileListAsync();

    public PaneSession CaptureSession() => new(
        [.. _state.Tabs.Select(TabSession.FromState)],
        _state.ActiveTabIndex);

    public void RestoreSession(PaneSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Tabs.Count == 0)
        {
            return;
        }

        var state = PaneState.Create(session.Tabs[0].ToState());
        foreach (var tab in session.Tabs.Skip(1))
        {
            state = state.AddTab(tab.ToState(), activate: false);
        }

        _state = state.Activate(Math.Clamp(session.ActiveTabIndex, 0, session.Tabs.Count - 1));
        RebuildTabs();
    }

    [RelayCommand]
    private async Task AddTabAsync()
    {
        var path = FileList.CurrentPath ?? _state.ActiveTab.Path;
        _state = _state.AddTab(TabState.Create(path));
        RebuildTabs();
        await ApplyActiveTabToFileListAsync();
    }

    [RelayCommand]
    private async Task CloseTabAsync(TabViewModel? tab)
    {
        // 파라미터 없이 호출되면(Ctrl+W) 활성 탭을 닫는다 — 바인딩 시점의 stale 참조 문제 회피.
        tab ??= ActiveTab;
        if (tab is null || _state.Tabs.Length <= 1)
        {
            return;
        }

        var index = IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        var wasActive = index == _state.ActiveTabIndex;
        _state = _state.CloseTab(index);
        RebuildTabs();
        if (wasActive)
        {
            await ApplyActiveTabToFileListAsync();
        }
    }

    [RelayCommand]
    private async Task NextTabAsync()
    {
        if (_state.Tabs.Length > 1)
        {
            await ActivateTabAtAsync((_state.ActiveTabIndex + 1) % _state.Tabs.Length);
        }
    }

    /// <summary>뷰의 탭 드래그 재정렬.</summary>
    public void MoveTab(TabViewModel tab, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var from = IndexOf(tab);
        if (from < 0)
        {
            return;
        }

        _state = _state.MoveTab(from, Math.Clamp(toIndex, 0, _state.Tabs.Length - 1));
        RebuildTabs();
    }

    /// <summary>크로스 페인 이동용: 탭을 떼어낸다. 마지막 탭이면 null(페인은 탭 ≥1).</summary>
    public async Task<TabState?> DetachTabAsync(TabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var index = IndexOf(tab);
        if (index < 0 || _state.Tabs.Length <= 1)
        {
            return null;
        }

        var detached = _state.Tabs[index];
        var wasActive = index == _state.ActiveTabIndex;
        _state = _state.CloseTab(index);
        RebuildTabs();
        if (wasActive)
        {
            await ApplyActiveTabToFileListAsync();
        }

        return detached;
    }

    /// <summary>크로스 페인 이동용: 탭을 받아 활성화한다.</summary>
    public async Task ReceiveTabAsync(TabState tab, int? insertIndex = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _state = insertIndex is { } index
            ? _state.InsertTab(index, tab)
            : _state.AddTab(tab);
        RebuildTabs();
        await ApplyActiveTabToFileListAsync();
    }

    /// <summary>이 페인을 미리보기 표면으로 전환한다 (Ctrl+Q).</summary>
    public void EnterPreview(PreviewViewModel preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Preview = preview;
        IsPreviewMode = true;
    }

    /// <summary>미리보기를 끄고 파일 목록으로 되돌린다.</summary>
    public void ExitPreview()
    {
        IsPreviewMode = false;
        Preview = null;
    }

    public void Dispose()
    {
        FileList.PropertyChanged -= OnFileListPropertyChanged;
        FileList.Dispose();
    }

    /// <summary>ListBox 선택 변경(사용자 탭 클릭) → 탭 전환.</summary>
    async partial void OnActiveTabChanged(TabViewModel? value)
    {
        if (_syncingTabs || value is null)
        {
            return;
        }

        var index = IndexOf(value);
        if (index >= 0 && index != _state.ActiveTabIndex)
        {
            await ActivateTabAtAsync(index);
        }
    }

    private async Task ActivateTabAtAsync(int index)
    {
        // 현재 경로/정렬은 OnFileListPropertyChanged가 상시 동기화하므로 전환 시 추가 저장이 필요 없다.
        _state = _state.Activate(index);
        RebuildTabs();
        await ApplyActiveTabToFileListAsync();
    }

    private async Task ApplyActiveTabToFileListAsync()
    {
        // 빠른 연속 탭 클릭으로 전환이 겹칠 수 있다 — 최신 전환만 가드 플래그를 내린다.
        // (네비게이션 자체는 FileListViewModel이 이전 로드를 취소하므로 마지막 클릭이 항상 이긴다.)
        var version = ++_switchVersion;
        var tab = _state.ActiveTab;
        _switchingTab = true;
        try
        {
            FileList.Sort = tab.Sort;
            await FileList.NavigateToAsync(tab.Path);
        }
        finally
        {
            if (version == _switchVersion)
            {
                _switchingTab = false;
            }
        }
    }

    private void OnFileListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 활성 탭 상태를 파일 목록과 상시 동기화한다 (탭 전환으로 인한 변경은 제외).
        if (_switchingTab)
        {
            return;
        }

        if (e.PropertyName == nameof(FileListViewModel.CurrentPath) && FileList.CurrentPath is { } path)
        {
            _state = _state.UpdateActiveTab(t => t with { Path = path });
            ActiveTab?.UpdatePath(path);
        }
        else if (e.PropertyName == nameof(FileListViewModel.Sort))
        {
            _state = _state.UpdateActiveTab(t => t with { Sort = FileList.Sort });
        }
    }

    private int IndexOf(TabViewModel tab)
    {
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (ReferenceEquals(Tabs[i], tab))
            {
                return i;
            }
        }

        return -1;
    }

    private void RebuildTabs()
    {
        _syncingTabs = true;
        try
        {
            Tabs = [.. _state.Tabs.Select(t => new TabViewModel(t.Path))];
            ActiveTab = Tabs[_state.ActiveTabIndex];
        }
        finally
        {
            _syncingTabs = false;
        }

        OnPropertyChanged(nameof(TabCount));
    }
}
