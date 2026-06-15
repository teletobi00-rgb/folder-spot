using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Explorer.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Explorer.App.Converters;

/// <summary>확장자 문자열 → 파일명 글자색 Brush. 지정색이 없으면 UnsetValue(기본 글자색 유지).</summary>
public sealed class ExtensionColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string extension && Application.Current is App)
        {
            var brush = App.Services.GetService<ExtensionColorMap>()?.BrushFor(extension);
            if (brush is not null)
            {
                return brush;
            }
        }

        // 지정색이 없으면 현재 테마의 기본 글자색(라이트=검정/다크=흰색)을 직접 반환한다.
        // (UnsetValue를 주면 TextBlock 기본값=검정으로 폴백돼 다크에서 안 보인다.)
        // 테마 토글 시에는 호출자가 목록을 새로고침해 이 값이 다시 계산되게 한다.
        return Application.Current?.TryFindResource("TextFillColorPrimaryBrush") ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
