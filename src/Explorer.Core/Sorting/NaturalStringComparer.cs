namespace Explorer.Core.Sorting;

/// <summary>
/// 자연 정렬 비교자: 숫자 구간은 수치로, 문자 구간은 현재 문화권 대소문자 무시로 비교한다.
/// ("file2" &lt; "file10", "가" &lt; "나")
/// ASCII 숫자(0-9)만 수치 비교한다 — Windows 탐색기(StrCmpLogicalW)와 동일한 의도적 제한.
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var xDigit = char.IsAsciiDigit(x[i]);
            var yDigit = char.IsAsciiDigit(y[j]);

            int result;
            if (xDigit && yDigit)
            {
                result = CompareDigitRuns(x, ref i, y, ref j);
            }
            else if (!xDigit && !yDigit)
            {
                result = CompareTextRuns(x, ref i, y, ref j);
            }
            else
            {
                // 숫자가 문자보다 먼저 온다 (Windows 탐색기 관례)
                return xDigit ? -1 : 1;
            }

            if (result != 0)
            {
                return result;
            }
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    private static int CompareDigitRuns(string x, ref int i, string y, ref int j)
    {
        var startX = i;
        var startY = j;
        while (i < x.Length && char.IsAsciiDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && char.IsAsciiDigit(y[j]))
        {
            j++;
        }

        var numX = x.AsSpan(startX, i - startX).TrimStart('0');
        var numY = y.AsSpan(startY, j - startY).TrimStart('0');

        if (numX.Length != numY.Length)
        {
            return numX.Length - numY.Length;
        }

        var compared = numX.CompareTo(numY, StringComparison.Ordinal);
        if (compared != 0)
        {
            return compared;
        }

        // 수치가 같으면(선행 0 차이) 원본 길이가 짧은 쪽 우선 — 결정적 순서 보장
        return (i - startX) - (j - startY);
    }

    private static int CompareTextRuns(string x, ref int i, string y, ref int j)
    {
        var startX = i;
        var startY = j;
        while (i < x.Length && !char.IsAsciiDigit(x[i]))
        {
            i++;
        }

        while (j < y.Length && !char.IsAsciiDigit(y[j]))
        {
            j++;
        }

        return x.AsSpan(startX, i - startX).CompareTo(
            y.AsSpan(startY, j - startY),
            StringComparison.CurrentCultureIgnoreCase);
    }
}
