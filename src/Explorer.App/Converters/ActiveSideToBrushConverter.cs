using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Explorer.Core.Workspace;

namespace Explorer.App.Converters;

/// <summary>활성 페인 테두리 강조: ConverterParameter("Left"/"Right")와 현재 ActiveSide가 일치하면 강조 브러시.</summary>
public sealed class ActiveSideToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PaneSide side
            && parameter is string expected
            && Enum.TryParse<PaneSide>(expected, out var target)
            && side == target)
        {
            return SystemColors.AccentColorBrush;
        }

        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
