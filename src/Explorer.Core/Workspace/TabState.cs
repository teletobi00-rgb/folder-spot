using Explorer.Core.Navigation;
using Explorer.Core.Sorting;

namespace Explorer.Core.Workspace;

/// <summary>탭 하나의 불변 상태. 페인이 탭을 소유한다(Total Commander 모델).</summary>
public sealed record TabState
{
    public required string Path { get; init; }

    public SortDescriptor Sort { get; init; } = SortDescriptor.Default;

    /// <summary>탭별 뒤로/앞으로 탐색 히스토리. 세션에는 저장하지 않는 in-memory 상태로,
    /// 페인이 공유하는 FileListViewModel을 탭 전환 시 이 값으로 복원한다.</summary>
    public NavigationHistory History { get; init; } = NavigationHistory.Empty;

    public static TabState Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TabState { Path = path };
    }
}
