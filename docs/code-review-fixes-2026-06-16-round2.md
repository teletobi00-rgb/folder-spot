# 코드 검토 후속 수정 내역 2차 (2026-06-16)

## 대상

전체 코드 베이스 재검토에서 확인한 인덱스 수명, FSW 증분 갱신, Unicode 검색 정규화, 파일 작업 충돌 처리 문제의 후속 수정 내용을 기록한다.

## 수정 내용

### P1: 인덱스 교체 중 기존 `FileIndex` 조기 폐기 방지

- `FileIndexCatalog`에 lease 기반 수명 관리를 추가했다.
- `Swap`은 더 이상 이전 인덱스를 즉시 반환/폐기하지 않고, 진행 중인 lease가 모두 해제된 뒤 폐기한다.
- 검색 팝업, 스냅샷 저장, FSW watcher가 비동기 작업 동안 lease를 보유하도록 변경했다.

### P2: FSW late event로 삭제 항목이 되살아나는 문제 방지

- `IndexPathUpdater.AddSinglePath`/`AddExistingPathTree`는 현재 디스크에 없는 경로를 더 이상 `AddKnownPath`로 추가하지 않고 기존 인덱스 항목을 제거한다.
- `AddKnownPath`는 journal 등 신뢰된 이벤트 경로용으로 유지했다.
- 생성/이동 이벤트는 파일시스템 가시성 레이스를 피하기 위해 짧게 1회 재확인한다. 그래도 없으면 phantom entry를 만들지 않는다.

### P2: Unicode NFC 검색 정규화 적용

- `FileIndex` 노드에 표시 이름과 별도인 `SearchName`을 추가했다.
- 검색 query와 인덱스 검색 키를 `NormalizationForm.FormC`로 정규화해 NFD 파일명과 NFC 입력이 매칭되도록 했다.
- 자식 이름 lookup 키도 정규화해 rename/remove 경로가 같은 정규화 정책을 따른다.

### P2/P3: 무충돌 copy/move의 조용한 덮어쓰기 방지

- 충돌 사전 스캔에서 충돌이 없던 항목은 `CollisionOption.Default`로 실행한다.
- 사용자가 명시적으로 덮어쓰기를 선택한 충돌 항목만 `CollisionOption.Overwrite`로 실행한다.
- `KeepBoth`와 `Skip` 그룹은 기존 동작을 유지한다.

## 추가/변경 테스트

- `FileIndexCatalogTests.Swap_KeepsPreviousIndexAliveUntilLeaseIsReleased`
- `FileIndexTests.Search_NormalizesComposedQueryAgainstDecomposedName`
- `IndexPathUpdaterTests.AddSinglePath_MissingPath_RemovesExistingEntry`
- `IndexPathUpdaterTests.AddExistingPathTree_MissingPath_RemovesExistingTree`
- `IndexPathUpdaterTests.AddKnownPath_MissingPath_CanSeedTrustedEventPath`
- `QueuedOperationExecutorTests.NoConflicts_ExecutesSingleDefaultGroup_WithoutPrompt`
- `QueuedOperationExecutorTests.ConflictDecisions_SplitIntoGroups`

## 검증

```powershell
dotnet test Explorer.slnx
```

결과: 전체 통과

- `Explorer.Core.Tests`: 197 passed
- `Explorer.Indexing.Tests`: 102 passed
- `Explorer.Preview.Tests`: 45 passed
- `Explorer.Shell.Tests`: 33 passed, 2 skipped
- `Explorer.App.Tests`: 116 passed
