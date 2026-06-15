namespace Explorer.Core.Settings;

/// <summary>파일 목록 보기 모드.</summary>
public enum FileViewMode
{
    /// <summary>자세히 — 이름/확장자/크기/날짜/특성 컬럼(정렬 가능).</summary>
    Details,

    /// <summary>간단히 — 아이콘+이름만 촘촘히, 열로 줄바꿈.</summary>
    List,

    /// <summary>썸네일 — 격자 형태의 미리보기 이미지 + 이름.</summary>
    Thumbnails,
}
