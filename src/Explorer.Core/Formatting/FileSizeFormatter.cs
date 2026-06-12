using System.Globalization;

namespace Explorer.Core.Formatting;

public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>바이트 수를 사람이 읽기 좋은 단위로 변환한다. 음수면 빈 문자열.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return string.Empty;
        }

        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value >= 100 ? "0" : "0.#";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }
}
