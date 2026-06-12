# 아키텍처 — Explorer

> 스택: .NET 10 (LTS) · WPF · CommunityToolkit.Mvvm · WPF UI(lepoco) · Vanara · Microsoft.Data.Sqlite

## 1. 솔루션 구조

```
Explorer.sln
├── Directory.Build.props          # LangVersion, Nullable, TreatWarningsAsErrors 공통
├── Directory.Packages.props       # NuGet 중앙 버전 관리
├── src/
│   ├── Explorer.Core/             # 도메인 모델·추상화·순수 로직 (외부 의존 0, TDD 100%)
│   ├── Explorer.Shell/            # Vanara 래퍼: 파일작업(IFileOperation)·컨텍스트메뉴·아이콘·D&D
│   ├── Explorer.Indexing/         # 인메모리 인덱스 + SQLite 스냅샷 + USN/FSW 소스
│   ├── Explorer.Preview/          # 렌더러 레지스트리 + IPreviewHandler 호스팅
│   ├── Explorer.App/              # WPF UI: Views/ViewModels/DI 컴포지션 루트
│   └── Explorer.Helper.Elevated/  # 관리자 권한 헬퍼 exe (MFT/USN 전용, named pipe IPC)
└── tests/
    ├── Explorer.Core.Tests/       ├── Explorer.Indexing.Tests/
    ├── Explorer.Shell.Tests/      ├── Explorer.Preview.Tests/
    └── Explorer.App.Tests/        # ViewModel 단위 + FlaUI 스모크(후반)
```

### 의존성 규칙 (위반 금지)

```
App → (Preview, Indexing, Shell) → Core
```
- **Core는 어디에도 의존하지 않는다.** WPF/Vanara/SQLite 참조 금지.
- Shell/Preview/Indexing은 서로 직접 참조하지 않고 Core의 인터페이스로만 통신.
- 모든 Win32/COM 호출은 Shell·Preview·Indexing 내부에 격리하고 Core 인터페이스(`IFileOperationService`, `IShellIconProvider`, `IFileIndex`…) 뒤에 숨긴다 → ViewModel은 mock으로 전부 테스트 가능.

## 2. 상태 모델 (핵심 설계 결정)

**페인이 탭을 소유한다 (Total Commander 모델).** Files 앱(탭이 페인을 소유)과 반대 — 사용자 스펙 "분할 화면마다 탭 추가"를 그대로 모델링.

```
WorkspaceState (record)
├── LeftPane:  PaneState ── Tabs: ImmutableArray<TabState>, ActiveTabIndex
├── RightPane: PaneState ── (동일)
├── ActivePane: Left | Right
└── IsDualMode: bool
TabState (record): Path, SortDescriptor, ColumnLayout?, SelectedPaths, ScrollPosition, IsPreviewTab
```

- 도메인 모델 전부 `record` / `ImmutableArray` — 탭 추가·닫기·이동·스왑은 **새 상태를 반환하는 순수 함수** (사용자 코딩 룰: 불변성).
- 가변 상태는 ViewModel(`[ObservableProperty]`)에만 존재.
- **탭 ≠ ViewModel 인스턴스**: 탭은 경량 `TabState`만 보유, `FileListViewModel`은 페인당 1개를 활성 탭 전환 시 상태 swap (탭 100개 세션 복원 시 메모리 폭증 방지 — 리스크 R-TABMEM).
- 미리보기 탭은 `IsPreviewTab=true`인 특수 탭으로 반대 페인에 삽입 (Ctrl+Q 토글).

## 3. 프로세스 구성

| 프로세스 | 권한 | 역할 |
|----------|------|------|
| `Explorer.exe` (메인) | 일반 | UI, 파일 작업, FSW 인덱싱, 검색 팝업, 트레이 |
| `Explorer.Helper.Elevated.exe` | 관리자(UAC 1회) | NTFS MFT 열거 + USN Journal tailing → named pipe로 델타 전송. 크래시해도 메인 무영향, 거부 시 비권한 폴백 |
| (v1.x 백로그) Preview Host | 일반 | 불안정 IPreviewHandler out-of-proc 격리 (PowerToys Peek 방식) |

## 4. 스레딩 모델

| 작업 | 스레드 | 규칙 |
|------|--------|------|
| UI/바인딩 | UI(STA) | 셸 컨텍스트 메뉴는 STA + HWND 오너 필수 |
| 폴더 열거 | ThreadPool | `IAsyncEnumerable` 배치 → Dispatcher로 청크 반영, CancellationToken 필수 |
| 아이콘 로딩 | 전용 큐 | **동기 로드 절대 금지** — placeholder → 비동기 채움, 확장자 단위 LRU 캐시 |
| 파일 작업 | 작업 큐 스레드 | IFileOperation 진행 sink → 진행 모델 → UI 디스패치 |
| 인덱싱 | `IHostedService` 백그라운드 | 초기 스캔 → FSW/USN tailing 전환 |
| SQLite 쓰기 | **단일 writer 스레드** | WAL 모드 + 배치 트랜잭션 (리스크 R-SQLITE) |
| 미리보기 | 디바운스 250ms + Cancellation | 선택 변경 폭주 시 이전 요청 취소 |

## 5. 데이터 흐름 예시 — 반대 페인 미리보기 (F9)

```
[활성 페인 SelectionChanged]
   → PreviewCoordinator (debounce 250ms, 이전 CancellationToken 취소)
   → IPreviewRendererRegistry.Resolve(ext)   # first-match: 내장 렌더러 → IPreviewHandler → 폴백(hex/정보)
   → 반대 페인의 PreviewTab ViewModel에 렌더 결과 게시
[Ctrl+Q 토글 OFF] → PreviewTab 제거, 이전 탭 상태 복원
```

## 6. 저장소 레이아웃

```
%AppData%\Explorer\
├── settings.json      # 일반 설정 (스키마 버전 필드 포함, 손상 시 기본값 폴백)
├── favorites.json     # 즐겨찾기 (파일/폴더)
├── colorrules.json    # 확장자 색상 규칙 (순서 배열)
├── keymap.json        # 키 바인딩 오버라이드
├── session.json       # WorkspaceState 직렬화 (세션 복원)
├── index\<volume>.db  # SQLite 인덱스 스냅샷 (볼륨별)
└── logs\              # Serilog 롤링 파일
```
포터블 모드(P1): exe 옆 `portable.flag` 존재 시 위 경로를 실행 폴더 하위로 전환.

## 7. 횡단 관심사

- **DI**: `Microsoft.Extensions.Hosting` 기반 `IHost`, 컴포지션 루트는 `App.xaml.cs` 한 곳.
- **에러 처리**: 전역 핸들러 3종(Dispatcher/AppDomain/TaskScheduler) → 로그 + 사용자 다이얼로그. HRESULT → 도메인 에러 매핑 테이블. 접근 거부는 skip-and-log + 부분 결과.
- **로깅**: Serilog 파일 싱크. 셸 확장/핸들러 크래시는 항목 식별 정보와 함께 기록(블랙리스트 근거).
- **검증**: 모든 외부 입력(경로 문자열, 설정 JSON, IPC 메시지, 검색 쿼리)은 경계에서 검증 — `PathUtils` 정규화(`\\?\` 포함), 설정 스키마 버전 체크.
- **파일 크기 규율**: 200~400줄 권장, 800줄 상한 (사용자 코딩 룰).
