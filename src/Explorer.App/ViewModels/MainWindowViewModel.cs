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
        KeyMap keyMap)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(driveSidebar);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(operationQueue);
        ArgumentNullException.ThrowIfNull(keyMap);
        _settings = settings;
        _themeService = themeService;
        Workspace = workspace;
        DriveSidebar = driveSidebar;
        Favorites = favorites;
        OperationQueue = operationQueue;
        KeyMap = keyMap;
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

    public DriveSidebarViewModel DriveSidebar { get; }

    public FavoritesViewModel Favorites { get; }

    public OperationQueueViewModel OperationQueue { get; }

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
    }

    /// <summary>설정 창에서 테마가 바뀐 뒤 툴바 토글 상태(툴팁)를 동기화한다.</summary>
    public void SyncThemeFromSettings() => CurrentTheme = _settings.Current.Theme;

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
