using Explorer.App.ViewModels;

namespace Explorer.App.Services;

/// <summary>두 페인의 항목을 이름으로 매칭해 비교 상태(이쪽만/최신/오래됨/동일)를 매긴다.</summary>
public static class PaneComparer
{
    public static void Compare(IReadOnlyList<FileItemViewModel> left, IReadOnlyList<FileItemViewModel> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftByName = ToLookup(left);
        var rightByName = ToLookup(right);

        foreach (var item in left)
        {
            item.CompareState = StateOf(item, rightByName);
        }

        foreach (var item in right)
        {
            item.CompareState = StateOf(item, leftByName);
        }
    }

    public static void Clear(IEnumerable<FileItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            item.CompareState = FileCompareState.None;
        }
    }

    private static Dictionary<string, FileItemViewModel> ToLookup(IReadOnlyList<FileItemViewModel> items)
    {
        var map = new Dictionary<string, FileItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            map.TryAdd(item.Name, item); // 같은 이름이 여럿이면 첫 항목 기준
        }

        return map;
    }

    private static FileCompareState StateOf(FileItemViewModel item, Dictionary<string, FileItemViewModel> other)
    {
        if (!other.TryGetValue(item.Name, out var match))
        {
            return FileCompareState.OnlyHere;
        }

        // 폴더는 이름만 비교(존재하면 동일 취급).
        if (item.IsDirectory || match.IsDirectory)
        {
            return FileCompareState.Same;
        }

        var byDate = item.Entry.DateModified.CompareTo(match.Entry.DateModified);
        if (byDate > 0)
        {
            return FileCompareState.Newer;
        }

        if (byDate < 0)
        {
            return FileCompareState.Older;
        }

        return item.Entry.Size == match.Entry.Size ? FileCompareState.Same : FileCompareState.Newer;
    }
}
