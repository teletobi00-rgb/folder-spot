using Explorer.Core.Sorting;

namespace Explorer.Core.Workspace;

/// <summary>탭 하나의 불변 상태. 페인이 탭을 소유한다(Total Commander 모델).</summary>
public sealed record TabState
{
    public required string Path { get; init; }

    public SortDescriptor Sort { get; init; } = SortDescriptor.Default;

    public static TabState Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new TabState { Path = path };
    }
}
