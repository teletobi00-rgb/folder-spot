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

    public MainWindowViewModel(ISettingsService settings, IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(themeService);
        _settings = settings;
        _themeService = themeService;
        _currentTheme = settings.Current.Theme;
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
}
