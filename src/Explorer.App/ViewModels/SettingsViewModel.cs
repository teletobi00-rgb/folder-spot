using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Explorer.App.Services;
using Explorer.Core.Settings;

namespace Explorer.App.ViewModels;

/// <summary>설정 창 한 화면의 편집 상태. 저장 시에만 ISettingsService에 반영한다.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ExtensionColorMap _colorMap;

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _useFastIndexing;

    /// <summary>저장이 실제로 적용됐는지(닫은 뒤 목록 새로고침 판단용).</summary>
    public bool Saved { get; private set; }

    public ObservableCollection<ExtensionColorRow> ColorRows { get; } = [];

    public IReadOnlyList<AppTheme> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public SettingsViewModel(ISettingsService settings, IThemeService theme, ExtensionColorMap colorMap)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(colorMap);
        _settings = settings;
        _theme = theme;
        _colorMap = colorMap;
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var current = _settings.Current;
        SelectedTheme = current.Theme;
        UseFastIndexing = current.UseFastIndexing;
        FillRows(current.ExtensionColors);
    }

    private void FillRows(IReadOnlyDictionary<string, string> colors)
    {
        ColorRows.Clear();
        foreach (var (extension, hex) in colors.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            ColorRows.Add(new ExtensionColorRow { Extension = extension, Hex = hex });
        }
    }

    [RelayCommand]
    private void AddRow() => ColorRows.Add(new ExtensionColorRow { Extension = string.Empty, Hex = "#808080" });

    [RelayCommand]
    private void RemoveRow(ExtensionColorRow? row)
    {
        if (row is not null)
        {
            ColorRows.Remove(row);
        }
    }

    [RelayCommand]
    private void ResetColors() => FillRows(ExtensionColorDefaults.Map);

    [RelayCommand]
    private void Save()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ColorRows)
        {
            var extension = row.Extension?.TrimStart('.').Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(extension) && !string.IsNullOrWhiteSpace(row.Hex))
            {
                builder[extension] = row.Hex.Trim();
            }
        }

        var colors = builder.ToImmutable();
        _settings.Update(s => s with
        {
            Theme = SelectedTheme,
            UseFastIndexing = UseFastIndexing,
            ExtensionColors = colors,
        });
        _theme.Apply(SelectedTheme);
        _colorMap.Reload();
        Saved = true;
    }
}

/// <summary>확장자 색상 편집 한 행.</summary>
public sealed partial class ExtensionColorRow : ObservableObject
{
    [ObservableProperty]
    private string _extension = string.Empty;

    [ObservableProperty]
    private string _hex = "#808080";
}
