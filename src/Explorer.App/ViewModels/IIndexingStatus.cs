namespace Explorer.App.ViewModels;

/// <summary>상태바가 바인딩하는 인덱싱 진행 상태의 읽기 전용 표면. 테스트에서 대체 가능하도록 분리.</summary>
public interface IIndexingStatus
{
    /// <summary>한 줄 요약(스캔 중 드라이브 또는 총 항목 수).</summary>
    string Summary { get; }

    /// <summary>드라이브별 상세(툴팁용).</summary>
    string Detail { get; }

    /// <summary>현재 스캔/대기 중인 드라이브가 있으면 true.</summary>
    bool IsIndexing { get; }

    /// <summary>인덱싱 대상이 하나라도 있으면 true(상태바 표시 여부).</summary>
    bool HasActivity { get; }
}
