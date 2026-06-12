namespace Explorer.Core.FileOperations;

/// <summary>Windows 파일명 규칙 검증과 고유 이름 생성.</summary>
public static class FileNameRules
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsValid(string? name, out string? reason)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "이름이 비어 있습니다.";
            return false;
        }

        if (name.Length > 255)
        {
            reason = "이름이 너무 깁니다 (최대 255자).";
            return false;
        }

        if (name.IndexOfAny(InvalidChars) >= 0)
        {
            reason = "사용할 수 없는 문자가 포함되어 있습니다: \\ / : * ? \" < > |";
            return false;
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            reason = "이름은 점이나 공백으로 끝날 수 없습니다.";
            return false;
        }

        // "CON", "con.txt" 같은 장치 예약 이름 (확장자가 붙어도 예약됨)
        var stem = name.Split('.', 2)[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            reason = $"'{stem}'은(는) 시스템 예약 이름입니다.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>"새 폴더", "새 폴더 (2)", "새 폴더 (3)" … 식으로 기존 이름과 겹치지 않는 이름을 만든다.</summary>
    public static string GenerateUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);

        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
