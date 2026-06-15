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
        foreach (var (extension, hex) in _settings.Current.ExtensionColors)
        {
            if (string.IsNullOrWhiteSpace(extension) || !TryParseColor(hex, out var color))
            {
                continue;
            }

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            builder[extension.TrimStart('.').ToLowerInvariant()] = brush;
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
