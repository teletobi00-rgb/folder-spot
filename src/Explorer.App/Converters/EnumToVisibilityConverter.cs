using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Explorer.App.Converters;

/// <summary>enum 값이 ConverterParameter(쉼표 구분 이름 목록 중 하나)와 같으면 Visible.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string names)
        {
            return Visibility.Collapsed;
        }

        var current = value.ToString();
        foreach (var name in names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
