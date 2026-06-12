using System.Collections.Immutable;

namespace Explorer.Core.Navigation;

/// <summary>브라우저 방식의 불변 탐색 히스토리. 모든 연산은 새 인스턴스를 반환한다.</summary>
public sealed record NavigationHistory
{
    public static NavigationHistory Empty { get; } = new();

    public ImmutableList<string> Entries { get; init; } = ImmutableList<string>.Empty;

    public int CurrentIndex { get; init; } = -1;

    public string? Current => CurrentIndex >= 0 && CurrentIndex < Entries.Count ? Entries[CurrentIndex] : null;

    public bool CanGoBack => CurrentIndex > 0;

    public bool CanGoForward => CurrentIndex >= 0 && CurrentIndex < Entries.Count - 1;

    /// <summary>새 경로 방문. 앞으로 가기 꼬리는 잘리고, 현재 경로와 같으면 변화 없음.</summary>
    public NavigationHistory Visit(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        var kept = CurrentIndex >= 0 ? Entries.GetRange(0, CurrentIndex + 1) : ImmutableList<string>.Empty;
        return new NavigationHistory
        {
            Entries = kept.Add(path),
            CurrentIndex = kept.Count,
        };
    }

    public NavigationHistory GoBack() =>
        CanGoBack ? this with { CurrentIndex = CurrentIndex - 1 } : this;

    public NavigationHistory GoForward() =>
        CanGoForward ? this with { CurrentIndex = CurrentIndex + 1 } : this;
}
