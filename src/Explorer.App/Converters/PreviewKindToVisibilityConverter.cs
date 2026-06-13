using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Explorer.Preview;

namespace Explorer.App.Converters;

/// <summary>현재 PreviewKind가 ConverterParameter(예: "Image")와 일치하면 Visible.</summary>
public sealed class PreviewKindToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PreviewKind kind && parameter is string expected
            && Enum.TryParse<PreviewKind>(expected, out var target))
        {
            return kind == target ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
