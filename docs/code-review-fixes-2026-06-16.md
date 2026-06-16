# 코드 검토 후속 수정 내역 (2026-06-16)

## 대상

이 문서는 코드 검토에서 P1~P2로 분류한 네 가지 항목의 후속 수정 내용을 기록한다.

## 수정 내용

### P1: USN helper 시작 대기 무한 블로킹 방지

- `UsnIndexSource`가 pipe 연결 후 `EnumDone` 또는 `Error`를 무한정 기다리지 않도록 열거 무응답 감시 타이머를 추가했다.
- 긴 MFT 열거 중 정상 작업이 타임아웃으로 오인되지 않도록 `UsnProtocol`에 `Heartbeat` 메시지를 추가했다.
- 권한 helper는 MFT 열거 중 2초마다 heartbeat를 보내며, 메인 프로세스는 heartbeat/batch 수신 시 시작 감시 타이머를 갱신한다.
- 연결만 하고 메시지를 보내지 않는 helper는 fallback으로 복귀한다.

### P2: 인덱싱 제외 규칙 일관 적용

- `IndexPathUpdater`를 추가해 증분 추가와 배치 필터링에서 `IndexExclusions`를 공통 적용한다.
- USN 초기 배치 수신 시 제외 대상 항목을 걸러낸 뒤 인덱스에 추가한다.
- FSW 생성/변경 이벤트도 제외 대상 경로면 인덱싱하지 않는다.

### P2: 디렉터리 이동 시 하위 항목 누락 방지

- 같은 부모 내 이름변경은 기존 O(1) rename 경로를 유지한다.
- 다른 폴더로 이동된 디렉터리는 기존 subtree를 제거한 뒤 새 위치의 현재 디렉터리 트리를 다시 열거해 하위 항목까지 인덱스에 반영한다.
- FSW와 USN 증분 경로 모두 같은 `IndexPathUpdater.AddExistingPathTree`를 사용한다.

### P2: AppSettings 테스트 실패 수정

- `AppSettings`는 `ImmutableDictionary`/`ImmutableArray`를 포함하므로 record 기본 동등성의 참조 비교에 기대면 테스트가 실패한다.
- 설정 서비스 테스트의 전체 설정 비교를 `BeEquivalentTo`로 바꿔 값 구조 기준으로 검증하도록 수정했다.

## 추가/변경 테스트

- `UsnProtocolTests.Heartbeat_Roundtrips`
- `UsnIndexSourceTests.ConnectedHelperWithoutMessages_TimesOutToFallback`
- `UsnIndexSourceTests.Heartbeat_KeepsEnumerationWaitAliveUntilEnumDone`
- `IndexPathUpdaterTests.FilterExcluded_RemovesItemsUnderExcludedTrees`
- `IndexPathUpdaterTests.AddExistingPathTree_Directory_AddsDescendants`
- `IndexPathUpdaterTests.AddExistingPathTree_ExcludedDirectory_DoesNotIndexTree`
- FSW/IndexingService 통합 테스트 루트를 제외 대상인 `%TEMP%` 밖으로 이동
- `JsonSettingsServiceTests`의 `AppSettings` 비교 방식 변경

## 검증

```powershell
dotnet test Explorer.slnx
```

결과: 전체 통과

- `Explorer.Core.Tests`: 197 passed
- `Explorer.Indexing.Tests`: 95 passed
- `Explorer.Preview.Tests`: 45 passed
- `Explorer.Shell.Tests`: 33 passed, 2 skipped
- `Explorer.App.Tests`: 116 passed
