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
        KeyMap keyMap)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(driveSidebar);
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(keyMap);
        _settings = settings;
        _themeService = themeService;
        Workspace = workspace;
        DriveSidebar = driveSidebar;
        Favorites = favorites;
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
        var next = CurrentTheme switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System,
        };

        var updated = _settings.Update(s => s with { Theme = next });
        _themeService.Apply(updated.Theme);
        CurrentTheme = updated.Theme;
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
