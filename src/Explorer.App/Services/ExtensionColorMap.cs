using System.Collections.Immutable;
using System.Windows.Media;
using Explorer.Core.Settings;

namespace Explorer.App.Services;

/// <summary>
/// 확장자(점 없는 소문자) → 파일명 글자색 Brush 맵. 설정에서 로드하며, 설정 변경 후
/// <see cref="Reload"/>로 다시 만들고 통지한다. Brush는 Freeze해 어느 스레드에서든 안전하다.
/// </summary>
public sealed class ExtensionColorMap
{
    private readonly ISettingsService _settings;
    private ImmutableDictionary<string, SolidColorBrush> _brushes =
        ImmutableDictionary<string, SolidColorBrush>.Empty;

    public ExtensionColorMap(ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Reload();

        // 테마 변경(툴바·트레이·설정·OS) 시 확장자색을 새 테마 변형으로 다시 만든다.
        // WPF-UI는 테마 적용 끝에 Changed를 동기로 올리므로, ToggleTheme의 페인 새로고침보다 먼저 _brushes가 갱신된다.
        Wpf.Ui.Appearance.ApplicationThemeManager.Changed += (_, _) => Reload();
    }

    /// <summary>맵이 갱신되면 발생 — 목록이 색을 다시 칠하도록.</summary>
    public event EventHandler? Changed;

    /// <summary>확장자에 지정색이 없으면 null(기본 글자색 사용).</summary>
    public Brush? BrushFor(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        var key = extension.TrimStart('.').ToLowerInvariant();
        return _brushes.TryGetValue(key, out var brush) ? brush : null;
    }

    /// <summary>현재 설정의 ExtensionColors로 맵을 다시 만든다.</summary>
    public void Reload()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        // 현재 적용된 실제 테마 감지
        var currentTheme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        bool isDark = currentTheme != Wpf.Ui.Appearance.ApplicationTheme.Light;

        var defaultPresets = ExtensionColorDefaults.Map;
        var themePresets = isDark ? ExtensionColorDefaults.DarkMap : ExtensionColorDefaults.LightMap;

        foreach (var (extension, hex) in _settings.Current.ExtensionColors)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                continue;
            }

            var key = extension.TrimStart('.').ToLowerInvariant();
            var finalHex = hex;

            // 설정된 색상이 기본 프리셋 색상과 동일하다면, 테마에 최적화된 색상으로 자동 변환
            if (defaultPresets.TryGetValue(key, out var defaultHex) &&
                string.Equals(hex, defaultHex, StringComparison.OrdinalIgnoreCase))
            {
                if (themePresets.TryGetValue(key, out var themeHex))
                {
                    finalHex = themeHex;
                }
            }

            if (!TryParseColor(finalHex, out var color))
            {
                continue;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            builder[key] = brush;
        }

        _brushes = builder.ToImmutable();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }
}
