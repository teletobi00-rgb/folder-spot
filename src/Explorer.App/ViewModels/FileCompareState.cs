namespace Explorer.App.ViewModels;

/// <summary>두 페인 비교 결과 — 행 색으로 표시한다.</summary>
public enum FileCompareState
{
    /// <summary>비교 안 함(기본).</summary>
    None,

    /// <summary>반대 페인에 같은 이름이 없음(이쪽에만 있음).</summary>
    OnlyHere,

    /// <summary>양쪽에 있고 이쪽이 더 최신(날짜).</summary>
    Newer,

    /// <summary>양쪽에 있고 이쪽이 더 오래됨.</summary>
    Older,

    /// <summary>양쪽에 있고 동일(날짜·크기, 폴더는 이름만).</summary>
    Same,
}
