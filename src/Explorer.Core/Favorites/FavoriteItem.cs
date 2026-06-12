namespace Explorer.Core.Favorites;

/// <summary>즐겨찾기 한 항목 (파일/폴더 모두 가능). 순서는 목록 내 위치가 결정한다.</summary>
public sealed record FavoriteItem
{
    public required string Path { get; init; }

    public bool IsDirectory { get; init; }

    /// <summary>사용자 지정 표시 이름. null이면 경로의 마지막 이름을 쓴다.</summary>
    public string? Label { get; init; }
}
