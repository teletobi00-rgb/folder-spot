using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.App.Services;
using Explorer.Core.Input;
using Explorer.Core.Settings;

namespace Explorer.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _themeService;
    private FileListViewModel _wiredFileList;

    [ObservableProperty]
    private AppTheme _currentTheme;

    public MainWindowViewModel(
        ISettingsService settings,
        IThemeService themeService,
        WorkspaceViewModel workspace,
        DriveSidebarViewModel driveSidebar,
        FavoritesViewModel favorites,
        OperationQueueViewModel operationQueue,
        ProgramLauncherViewModel programLauncher,
        KeyMap keyMap,
        IIndexingStatus indexingStatus,
        IResourceMonitor resourceMonitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(driveSidebar);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(programLauncher);
        ArgumentNullException.ThrowIfNull(keyMap);
        ArgumentNullException.ThrowIfNull(indexingStatus);
        ArgumentNullException.ThrowIfNull(resourceMonitor);
        _settings = settings;
        _themeService = themeService;
        Workspace = workspace;
        DriveSidebar = driveSidebar;
        Favorites = favorites;
        OperationQueue = operationQueue;
        ProgramLauncher = programLauncher;
        KeyMap = keyMap;
        IndexingStatus = indexingStatus;
        ResourceMonitor = resourceMonitor;
        AddressBar = new AddressBarViewModel();
        _currentTheme = settings.Current.Theme;

        DriveSidebar.DriveOpenRequested += (_, path) => _ = Workspace.ActiveFileList.NavigateToAsync(path);
        Favorites.FolderOpenRequested += (_, path) => _ = Workspace.ActiveFileList.NavigateToAsync(path);
        AddressBar.NavigationRequested += (_, path) => _ = Workspace.ActiveFileList.NavigateToAsync(path);

        // 주소창은 항상 "활성 페인"의 경로를 따라간다 — 활성 페인이 바뀌면 구독을 옮긴다.
        _wiredFileList = Workspace.ActiveFileList;
        _wiredFileList.PropertyChanged += OnActiveFileListPropertyChanged;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public WorkspaceViewModel Workspace { get; }

    /// <summary>인덱싱 진행 상태(상태바 표시용).</summary>
    public IIndexingStatus IndexingStatus { get; }

    /// <summary>CPU·메모리 사용량(상태바 표시용, 옵트인).</summary>
    public IResourceMonitor ResourceMonitor { get; }

    public DriveSidebarViewModel DriveSidebar { get; }

    public FavoritesViewModel Favorites { get; }

    public OperationQueueViewModel OperationQueue { get; }

    public ProgramLauncherViewModel ProgramLauncher { get; }

    public KeyMap KeyMap { get; }

    public AddressBarViewModel AddressBar { get; }

    /// <summary>창 표시 후 호출: 드라이브/즐겨찾기 목록 채우고 세션 복원(없으면 기본 폴더).</summary>
    public async Task InitializeAsync()
    {
        DriveSidebar.RefreshCommand.Execute(null);
        Favorites.Initialize();
        await Workspace.RestoreSessionAsync(_settings.Current.Session);
    }

    /// <summary>종료 시 세션 저장.</summary>
    public void SaveSession()
    {
        var session = Workspace.CaptureSession();
        _settings.Update(s => s with { Session = session });
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        // 다크 ↔ 라이트 2상태 토글. System은 순환에서 제외한다 — OS가 다크면 System이 다크처럼 보여
        // "한 번 더 클릭해야 바뀌는" 헛클릭이 생기기 때문(System은 설정 창에서 명시 선택).
        // 기준을 settings.Current로 두어 설정 창에서의 테마 변경과도 항상 동기화된다.
        var next = _settings.Current.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        var updated = _settings.Update(s => s with { Theme = next });
        _themeService.Apply(updated.Theme);
        CurrentTheme = updated.Theme;

        // 색 지정이 없는 항목의 기본 글자색은 변환 시점의 테마 색으로 굳으므로, 테마가 바뀌면
        // 양쪽 페인을 새로고침해 새 테마 색으로 다시 계산되게 한다.
        Workspace.LeftPane.FileList.RefreshCommand.Execute(null);
        Workspace.RightPane.FileList.RefreshCommand.Execute(null);
    }

    /// <summary>설정 창에서 테마가 바뀐 뒤 툴바 토글 상태(툴팁)를 동기화한다.</summary>
    public void SyncThemeFromSettings() => CurrentTheme = _settings.Current.Theme;

    /// <summary>현재 보기 모드(양쪽 페인 동일). 토글 활성 표시에 사용.</summary>
    public FileViewMode ViewMode => Workspace.LeftPane.FileList.ViewMode;

    public int ThumbnailSize => Workspace.LeftPane.FileList.ThumbnailSize;

    /// <summary>보기 모드(자세히/간단히/썸네일)를 양쪽 페인에 적용하고 저장한다.</summary>
    [RelayCommand]
    private void SetViewMode(string? mode)
    {
        if (!Enum.TryParse<FileViewMode>(mode, out var viewMode))
        {
            return;
        }

        Workspace.LeftPane.FileList.ViewMode = viewMode;
        Workspace.RightPane.FileList.ViewMode = viewMode;
        _settings.Update(s => s with { ViewMode = viewMode });
        OnPropertyChanged(nameof(ViewMode));
    }

    /// <summary>썸네일 크기를 적용하고(자동으로 썸네일 모드 전환) 저장한다.</summary>
    [RelayCommand]
    private void SetThumbnailSize(string? size)
    {
        if (!int.TryParse(size, out var px))
        {
            return;
        }

        Workspace.LeftPane.FileList.ThumbnailSize = px;
        Workspace.RightPane.FileList.ThumbnailSize = px;
        Workspace.LeftPane.FileList.ViewMode = FileViewMode.Thumbnails;
        Workspace.RightPane.FileList.ViewMode = FileViewMode.Thumbnails;
        _settings.Update(s => s with { ThumbnailSize = px, ViewMode = FileViewMode.Thumbnails });
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(ThumbnailSize));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkspaceViewModel.ActiveFileList))
        {
            return;
        }

        _wiredFileList.PropertyChanged -= OnActiveFileListPropertyChanged;
        _wiredFileList = Workspace.ActiveFileList;
        _wiredFileList.PropertyChanged += OnActiveFileListPropertyChanged;
        AddressBar.SetCurrentPath(_wiredFileList.CurrentPath);
    }

    private void OnActiveFileListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileListViewModel.CurrentPath))
        {
            AddressBar.SetCurrentPath(Workspace.ActiveFileList.CurrentPath);
        }
    }
}
