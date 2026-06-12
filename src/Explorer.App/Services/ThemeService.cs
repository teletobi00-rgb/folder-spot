using Explorer.Core.Settings;
using Wpf.Ui.Appearance;

namespace Explorer.App.Services;

public interface IThemeService
{
    void Apply(AppTheme theme);
}

public sealed class WpfUiThemeService : IThemeService
{
    public void Apply(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }
}
