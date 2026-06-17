using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Explorer.App.ViewModels;

namespace Explorer.App.Converters;

/// <summary>비교 상태 → 마커(세로 라인·도트) 색. 불투명 단색이라 다크/라이트 배경 위에서 모두 또렷하다.</summary>
public sealed class CompareStateToBrushConverter : IValueConverter
{
    // 4px 세로 라인 / 8px 도트 배지용 불투명 단색.
    private static readonly SolidColorBrush OnlyHere = Frozen(0xFF, 0xFF, 0xA5, 0x00); // 앰버 — 이쪽에만
    private static readonly SolidColorBrush Newer = Frozen(0xFF, 0x3C, 0xC8, 0x4A);    // 초록 — 이쪽이 최신
    private static readonly SolidColorBrush Older = Frozen(0xFF, 0x4A, 0x90, 0xE2);    // 파랑 — 이쪽이 오래됨
    private static readonly SolidColorBrush None = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FileCompareState state
            ? state switch
            {
                FileCompareState.OnlyHere => OnlyHere,
                FileCompareState.Newer => Newer,
                FileCompareState.Older => Older,
                _ => None,
            }
            : None;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
