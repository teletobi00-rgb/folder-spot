namespace Explorer.Indexing;

/// <summary>드라이브 한 곳의 인덱싱 단계.</summary>
public enum DriveIndexPhase
{
    /// <summary>대기 — 아직 스캔 전.</summary>
    Pending,

    /// <summary>스캔 중 — 초기 열거 진행.</summary>
    Scanning,

    /// <summary>감시 중 — 초기 인덱싱 완료, 증분(FSW/USN) 추적.</summary>
    Watching,

    /// <summary>생략 — 스냅샷이 최신이라 초기 스캔을 건너뜀(이미 인덱싱됨).</summary>
    Skipped,

    /// <summary>일부만 — 상한(시간·노드 수) 도달로 부분 인덱싱(주로 대용량 네트워크).</summary>
    Partial,

    /// <summary>실패 — 스캔 중 오류로 건너뜀.</summary>
    Error,
}

/// <summary>드라이브별 인덱싱 진행 상태(불변). <see cref="ItemCount"/>는 알 수 없으면 0.</summary>
public sealed record DriveIndexProgress(string Root, DriveIndexPhase Phase, long ItemCount);
