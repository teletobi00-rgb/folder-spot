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

        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
