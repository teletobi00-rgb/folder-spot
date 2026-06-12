namespace Explorer.Core.Favorites;

/// <summary>파일/폴더 즐겨찾기 저장소. 모든 변경은 즉시 영속화되고 <see cref="Changed"/>를 발생시킨다.</summary>
public interface IFavoritesService
{
    IReadOnlyList<FavoriteItem> Items { get; }

    /// <remarks>변경을 일으킨 호출자의 스레드에서 발생한다 — UI를 갱신하는 구독자는 UI 스레드에서만 변경 메서드를 호출할 것.</remarks>
    event EventHandler? Changed;

    /// <summary>디스크에서 즐겨찾기를 읽는다. 파일이 없거나 손상이면 빈 목록.</summary>
    void Load();

    /// <summary>
    /// 항목 추가 (경로 기준 대소문자 무시 중복은 무시).
    /// <paramref name="isDirectory"/>가 null이면 디스크에서 자동 판별한다.
    /// </summary>
    void Add(string path, bool? isDirectory = null);

    void Remove(string path);

    /// <summary>항목을 새 위치로 옮긴다 (인덱스는 범위로 클램프).</summary>
    void Move(string path, int newIndex);
}
