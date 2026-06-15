using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Explorer.App.Converters;

/// <summary>#RRGGBB 문자열 → SolidColorBrush (설정 창 색상 스와치 미리보기용). 잘못된 값은 투명.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex) is Color color)
                {
                    return new SolidColorBrush(color);
                }
            }
            catch (FormatException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
