using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Explorer.App.ViewModels;

namespace Explorer.App.Converters;

/// <summary>비교 상태 → 행 배경 반투명 틴트. 반투명이라 다크/라이트 배경 위에서 모두 자연스럽다.</summary>
public sealed class CompareStateToBrushConverter : IValueConverter
{
    // alpha ~0x30 틴트.
    private static readonly SolidColorBrush OnlyHere = Frozen(0x30, 0xFF, 0xA5, 0x00); // 앰버 — 이쪽에만
    private static readonly SolidColorBrush Newer = Frozen(0x30, 0x3C, 0xC8, 0x4A);    // 초록 — 이쪽이 최신
    private static readonly SolidColorBrush Older = Frozen(0x28, 0x4A, 0x90, 0xE2);    // 파랑 — 이쪽이 오래됨
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
