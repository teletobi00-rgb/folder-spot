namespace Explorer.Core.FileOperations;

/// <summary>이름 충돌 시 셸 엔진에 지시할 정책.</summary>
public enum CollisionOption
{
    /// <summary>기존(Phase 2) 동작 — 셸 기본 처리.</summary>
    Default,

    /// <summary>조용히 덮어쓴다 (사전 충돌 해소를 마친 그룹용).</summary>
    Overwrite,

    /// <summary>충돌 시 자동으로 새 이름 부여 ("- 복사본" 등).</summary>
    KeepBoth,
}

/// <summary>작업 한 건이 완료될 때마다 보고되는 실제 결과 항목 (Undo 기록용).</summary>
public sealed record CompletedItem
{
    public required OperationKind Kind { get; init; }

    public required string SourcePath { get; init; }

    /// <summary>실제 생성/이동된 경로. 삭제(휴지통)는 휴지통 내 물리 항목 경로($R...).</summary>
    public string? NewPath { get; init; }
}

/// <summary>진행/완료 콜백 — 작업 엔진 스레드에서 호출되므로 구현은 스레드 마샬링을 책임진다.</summary>
public interface IOperationEvents
{
    void OnProgress(int percent);

    void OnItemCompleted(CompletedItem item);
}

/// <summary>서비스 호출에 덧붙이는 선택적 실행 컨텍스트 (null이면 Phase 2와 동일하게 동작).</summary>
public sealed class FileOperationContext
{
    public CollisionOption Collision { get; init; } = CollisionOption.Default;

    public OperationControl? Control { get; init; }

    public IOperationEvents? Events { get; init; }
}
