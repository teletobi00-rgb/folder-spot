using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Explorer.App.Converters;

/// <summary>enum 값이 ConverterParameter와 같으면 Primary(강조), 아니면 Secondary. 툴바 토글 버튼 활성 표시용.</summary>
public sealed class EnumToAppearanceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
