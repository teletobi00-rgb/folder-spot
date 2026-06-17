namespace Explorer.App.ViewModels;

/// <summary>상태바가 바인딩하는 리소스 모니터의 읽기 전용 표면. 테스트에서 대체 가능하도록 분리.</summary>
public interface IResourceMonitor
{
    /// <summary>표시 사용 여부(옵트인). false면 상태바에서 숨긴다.</summary>
    bool IsEnabled { get; }

    /// <summary>요약 문자열(예: "CPU 12% · 메모리 340MB").</summary>
    string Summary { get; }
}
