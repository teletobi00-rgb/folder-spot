using System.Globalization;
using System.Windows.Data;
using Explorer.Core.Formatting;

namespace Explorer.App.Converters;

/// <summary>바이트 수(long)를 사람이 읽기 좋은 크기 문자열로.</summary>
public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? FileSizeFormatter.Format(bytes) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
