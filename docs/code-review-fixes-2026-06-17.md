# Code Review Fixes - 2026-06-17

## Review Scope

전체 코드 베이스 재검토 중 인덱싱 파이프라인의 네트워크 스캔, 진행 상태 이벤트, 제외 경로 rename 처리에서 실제 검색 정확도에 영향을 줄 수 있는 결함을 확인했다. 이번 수정은 해당 결함을 좁게 고치고 회귀 테스트를 추가하는 데 집중했다.

## Findings Fixed

### 1. 네트워크 startup-skip 재스캔이 삭제 파일을 정리하지 않음

- 문제: 최신 snapshot으로 로컬 재스캔을 생략한 뒤 네트워크 루트를 live index에 직접 추가해, 앱 종료 중 삭제된 네트워크 파일이 계속 검색 결과에 남을 수 있었다.
- 수정: 네트워크 루트를 먼저 staging `FileIndex`에 스캔하고, complete scan이면 live index의 해당 root subtree를 제거한 뒤 staging 결과로 교체한다.
- 영향 파일: `src/Explorer.Indexing/IndexingService.cs`

### 2. 네트워크 `maxItems` 도달이 완료 상태로 표시됨

- 문제: `RecursiveScanSource`가 상한 도달 여부를 반환하지 않아, 일부만 인덱싱된 네트워크 루트가 `Watching` 상태로 표시됐다.
- 수정: `ScanResult.Count`, `ScanResult.HitLimit` 반환 타입을 추가했다. `HitLimit`이면 `DriveIndexPhase.Partial`로 표시하고 complete snapshot 변경으로 저장하지 않는다.
- 영향 파일: `src/Explorer.Indexing/Sources/RecursiveScanSource.cs`, `src/Explorer.Indexing/IndexingService.cs`

### 3. 시간 제한 cancellation 시 마지막 partial batch가 유실됨

- 문제: cancellation이 발생하면 마지막 1~4999개 pending batch가 final flush 전에 버려질 수 있었다.
- 수정: `OperationCanceledException` 처리에서 pending batch를 flush한 뒤 cancellation을 다시 throw한다. 네트워크 timeout 경로는 staging에 들어온 항목을 live index에 merge하고 `Partial`로 표시한다.
- 영향 파일: `src/Explorer.Indexing/Sources/RecursiveScanSource.cs`, `src/Explorer.Indexing/IndexingService.cs`

### 4. 같은 부모 내 rename이 exclusion 규칙을 우회함

- 문제: indexed 폴더/파일을 `.git`, `node_modules` 같은 제외 이름으로 rename하면 subtree가 검색 결과에 남을 수 있었다.
- 수정: FSW rename과 USN rename 적용 경로 모두 새 경로가 excluded이면 old subtree를 제거하고 rename을 중단한다.
- 영향 파일: `src/Explorer.Indexing/Sources/FswIndexSource.cs`, `src/Explorer.Indexing/IndexingService.cs`

### 5. progress event 구독자 예외가 indexing pipeline을 중단할 수 있음

- 문제: `DriveProgressChanged` 구독자 예외가 `RunPipelineAsync`까지 전파되어 전체 인덱싱 상태가 오류로 바뀔 수 있었다.
- 수정: 구독자별 호출을 격리하고 예외는 debug log로만 남긴다.
- 영향 파일: `src/Explorer.Indexing/IndexingService.cs`

## Tests Added

- `RecursiveScanSourceTests.Scan_StopsAtMaxItems`: `HitLimit` 검증 추가.
- `FswIndexSourceTests.RenamedDirectory_ToExcludedName_RemovesSubtree`: rename-to-excluded 회귀 검증.
- `IndexingServiceTests.DriveProgressSubscriberException_DoesNotStopPipeline`: progress subscriber 예외 격리 검증.
- `IndexingServiceTests.FreshSnapshot_NetworkRoot_ReplacesStaleSubtree`: startup-skip 네트워크 stale 삭제 검증.
- `IndexingServiceTests.NetworkRoot_HittingMaxItems_IsMarkedPartial`: 네트워크 cap 도달 시 partial phase 검증.

## Verification

- `dotnet test tests/Explorer.Indexing.Tests/Explorer.Indexing.Tests.csproj`
  - Passed: 107
- `dotnet test Explorer.slnx`
  - Passed: Core 197, Indexing 107, Preview 45, Shell 33 passed / 2 skipped, App 128

