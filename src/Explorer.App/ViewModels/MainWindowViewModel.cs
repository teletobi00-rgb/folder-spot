using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.App.Services;
using Explorer.Core.Settings;

namespace Explorer.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _themeService;

    [ObservableProperty]
    private AppTheme _currentTheme;

    public MainWindowViewModel(
        ISettingsService settings,
        IThemeService themeService,
        FileListViewModel fileList,
        DriveSidebarViewModel driveSidebar)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(fileList);
        ArgumentNullException.ThrowIfNull(driveSidebar);
        _settings = settings;
        _themeService = themeService;
        FileList = fileList;
        DriveSidebar = driveSidebar;
        AddressBar = new AddressBarViewModel();
        _currentTheme = settings.Current.Theme;

        DriveSidebar.DriveOpenRequested += (_, path) => _ = FileList.NavigateToAsync(path);
        AddressBar.NavigationRequested += (_, path) => _ = FileList.NavigateToAsync(path);
        FileList.PropertyChanged += OnFileListPropertyChanged;
    }

    public FileListViewModel FileList { get; }

    public DriveSidebarViewModel DriveSidebar { get; }

    public AddressBarViewModel AddressBar { get; }

    /// <summary>창 표시 후 호출: 드라이브 목록 채우고 시작 폴더로 이동한다.</summary>
    public async Task InitializeAsync()
    {
        DriveSidebar.RefreshCommand.Execute(null);
        await FileList.NavigateToAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .ConfigureAwait(false);
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

    private void OnFileListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileListViewModel.CurrentPath))
        {
            AddressBar.SetCurrentPath(FileList.CurrentPath);
        }
    }
}
