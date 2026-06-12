# Task List — Explorer (단계별 구현 계획)

> 각 Phase는 종료 시점에 "그날 바로 쓸 수 있는" 실행 가능한 앱 상태를 유지한다.
> 태스크 단위는 30~180분. **TDD** 표시는 테스트 선행(RED→GREEN→REFACTOR) 대상.

## 버전 컷라인

| 버전 | Phase | 비고 |
|------|-------|------|
| **v0.1** 데일리 드라이버 | 0~4 | 듀얼 페인+탭+기본 작업+셸 통합+TC 키보드+즐겨찾기 |
| **v0.5** | +5~7 | 작업 큐/충돌/Undo + 인덱스 인프라 |
| **v1.0** 완성형 | +8~9 | 글로벌 검색 + 미리보기 |
| **v1.x** | +10~13 | 커스터마이즈·검색 고도화·유틸리티·견고성 |

---

## Phase 0: 스캐폴딩 & 컴포지션 루트

**목표:** 빌드되는 솔루션 + DI + 설정 + 로깅 + WPF UI 빈 셸. *(선행: .NET SDK 10 설치 — `winget install Microsoft.DotNet.SDK.10`, git init)*
> ✅ 2026-06-12 완료. 비고: .NET 9(STS)가 2026-05 EOL이라 **.NET 10 LTS로 타겟 상향**, 솔루션 파일은 SDK 10 기본인 `.slnx` 포맷.

- [ ] 0.1 솔루션 + 6개 프로젝트 생성, `Directory.Build.props`(Nullable, TreatWarningsAsErrors)
- [ ] 0.2 `Directory.Packages.props` NuGet 중앙 관리 (wpfui, CommunityToolkit.Mvvm, Vanara, Microsoft.Data.Sqlite, AvalonEdit, SharpCompress, H.NotifyIcon, Hosting/Serilog, xUnit/FluentAssertions/NSubstitute/FlaUI)
- [ ] 0.3 `IHost` 기반 DI 컴포지션 루트 (`App.xaml.cs`)
- [ ] 0.4 WPF UI `FluentWindow` + 다크/라이트 테마 초기화
- [ ] 0.5 `ISettingsService` + JSON 백엔드(스키마 버전, 손상 시 기본값 폴백) — **TDD**
- [ ] 0.6 Serilog 파일 싱크
- [ ] 0.7 전역 예외 핸들러 3종 → 로그 + 다이얼로그
- [ ] 0.8 GitHub Actions: build + test
- [ ] 0.9 `.editorconfig`, `.gitignore`

**DoD:** 무경고 빌드 · 빈 Fluent 윈도우 + 테마 토글 · 설정 생성/로드/폴백 · 의도적 예외 시 크래시 없음

---

## Phase 1: 단일 페인 탐색 (읽기 전용)

**목표:** 드라이브 사이드바 + 주소창 + 컬럼 목록으로 폴더 탐색.

Core — **전부 TDD**
- [ ] 1.1 `FileEntry` record + 정규화
- [ ] 1.2 `IFileSystemEnumerator` (`System.IO.Enumeration`, 접근거부 skip-and-log)
- [ ] 1.3 `SortDescriptor` + 폴더 우선 + **자연 정렬**("file2"<"file10")
- [ ] 1.4 `NavigationHistory` (불변 back/forward)
- [ ] 1.5 `PathUtils` (정규화·검증·`\\?\` 대비)

Shell
- [ ] 1.6 `IDriveProvider` (고정/이동식/네트워크 구분, 용량) — TDD
- [ ] 1.7 `IShellIconProvider` (Vanara, 비동기) — 계약만 TDD
- [ ] 1.8 `IIconCache` (확장자 단위 + LRU) — **TDD**

ViewModel — **전부 TDD (mock 주입)**
- [ ] 1.9 `FileListViewModel` (경로/항목/선택/정렬/네비게이션 명령)
- [ ] 1.10 `DriveSidebarViewModel` · 1.11 `AddressBarViewModel`

View — 수동 검증
- [ ] 1.12 `FileListView` — `ListView+GridView` + **가상화 필수**(`Recycling`)
- [ ] 1.13 컬럼 5종 표시·정렬 헤더·리사이즈
- [ ] 1.14 드라이브 사이드바 · 1.15 주소창+툴바(뒤/앞/위)
- [ ] 1.16 더블클릭 진입/실행(ShellExecute), Enter/Backspace/Alt+화살표
- [ ] 1.17 비동기 로딩 인디케이터 + 빈 폴더/접근 거부 상태 UI

**DoD:** `C:\Windows\System32`(10만+) 잰크 없는 스크롤 · 자연 정렬 · 접근 거부 시 부분 결과 · 아이콘 비동기(잰크 금지)
**병렬:** Core(1.1~1.5) ∥ Shell(1.6~1.8), 이후 VM → View

---

## Phase 2: 쓰기 작업 + 셸 통합

**목표:** 복사/이동/삭제(휴지통)/이름변경/새 폴더 + 우클릭 셸 메뉴 + 탐색기 양방향 D&D.

작업 엔진
- [ ] 2.1 `IFileOperationService` 계약 (진행·취소·결과) — **TDD**
- [ ] 2.2 Vanara `ShellFileOperations` 구현 (휴지통, 진행 sink) — temp dir 통합 테스트
- [ ] 2.3 HRESULT → 도메인 에러 매핑 — **TDD**
- [ ] 2.4 클립보드 `CF_HDROP` + DropEffect(복사/잘라내기) — **TDD**

컨텍스트 메뉴
- [ ] 2.5 `IShellContextMenuService` (Vanara, STA+HWND 오너) — 수동
- [ ] 2.6 자체 명령 병합

D&D
- [ ] 2.7 페인 내/간 드래그 (Shift=이동, Ctrl=복사)
- [ ] 2.8 탐색기 ↔ 앱 양방향 (`IDataObject`/CF_HDROP)
- [ ] 2.9 자기/하위 폴더 드롭 차단 — **TDD**

명령/UX
- [ ] 2.10 Ctrl+C/X/V, Del/Shift+Del, F2, F7 — 활성화 조건 **TDD**
- [ ] 2.11 인라인 rename — 수동
- [ ] 2.12 작업 후 부분 갱신(전체 리로드 회피) — **TDD**

**DoD:** 전 기본 작업 + 진행 표시 · 네이티브 우클릭 메뉴 실행 · 양방향 D&D · 권한/사용중 에러 명확, 크래시 없음
**병렬:** 엔진 ∥ 메뉴 ∥ D&D 3트랙 · **리스크:** R-SHELLCRASH(메뉴 try/catch+재생성)

---

## Phase 3: 듀얼 페인 + 페인별 탭

**목표:** 2분할(토글·스플리터) + **페인이 탭 소유**(TC 모델) + breadcrumb + 세션 복원.

상태 모델 — **전부 TDD (불변 조작 전수)**
- [ ] 3.1 `TabState` · 3.2 `PaneState`(탭 add/close/move/reorder 순수 함수) · 3.3 `WorkspaceState`

ViewModel — **전부 TDD**
- [ ] 3.4 `PaneViewModel` — 탭은 경량 TabState만, FileListViewModel은 활성 탭 swap(R-TABMEM)
- [ ] 3.5 `TabViewModel` · 3.6 `WorkspaceViewModel`(활성 페인, 단일/듀얼) · 3.7 `BreadcrumbViewModel`

View — 수동
- [ ] 3.8 듀얼 레이아웃 + GridSplitter + 활성 페인 강조
- [ ] 3.9 탭 스트립(+, 닫기) · 3.10 탭 드래그 재정렬/반대 페인 이동
- [ ] 3.11 breadcrumb(세그먼트 클릭·형제 드롭다운, 텍스트 모드 전환) · 3.12 포커스 → 활성 페인 갱신

세션
- [ ] 3.13 `WorkspaceState` 직렬화 — 시작 복원/종료 저장 — **TDD**

**DoD:** 페인별 독립 탭 전 조작 · breadcrumb · 재시작 시 탭/경로/활성 페인 복원 · 활성 페인 시각 명확
**병렬:** Phase 2와 직교(동시 진행 가능)

---

## Phase 4: 크로스 페인 + TC 키보드 + 즐겨찾기 → ⭐ v0.1

- [ ] 4.1 크로스 페인 해석기(소스=활성 선택, 타겟=반대 페인 경로) — **TDD**
- [ ] 4.2 F5 복사 / F6 이동 — **TDD**
- [ ] 4.3 Tab 페인 전환 · Ctrl+U 스왑 — **TDD**
- [ ] 4.4 Ctrl+←/→ 반대 페인에 열기 — **TDD**
- [ ] 4.5 `IKeyBindingService` 중앙 키맵(JSON) — **TDD**
- [ ] 4.6 `IFavoritesService` — **파일+폴더** 핀, 재정렬, 영속 — **TDD**
- [ ] 4.7 즐겨찾기 사이드바(클릭=이동/실행, 드래그 추가) — 수동
- [ ] 4.8 즐겨찾기 우클릭(제거/이름/순서) — 수동

**DoD:** TC 계약 전체 동작 · 파일/폴더 즐겨찾기 영속 · 키맵 중앙 관리
**병렬:** 크로스 페인(4.1~4.5) ∥ 즐겨찾기(4.6~4.8)

---

## Phase 5: 작업 큐 UI + 충돌 + Undo

- [ ] 5.1 `IOperationQueue` 상태 머신(Pending/Running/Paused/Done/Failed) — **TDD**
- [ ] 5.2 진행 모델(바이트/항목, 이동평균 속도, ETA) — **TDD**
- [ ] 5.3 일시정지/재개/취소(IFileOperation sink 연동)
- [ ] 5.4 큐 패널 UI — 수동
- [ ] 5.5 충돌 정보 모델(양측 메타+썸네일) — **TDD**
- [ ] 5.6 커스텀 충돌 다이얼로그(덮어쓰기/건너뛰기/둘 다 유지+모두 적용) — 수동
- [ ] 5.7 `IUndoService` 역연산(move/rename/휴지통 복원) — **TDD**
- [ ] 5.8 Ctrl+Z + 작업 히스토리 뷰

**DoD:** 대용량 복사 진행/속도/ETA/일시정지 · 충돌 일괄 처리 · Ctrl+Z 복원
**병렬:** 큐 ∥ 충돌 ∥ Undo 3트랙

---

## Phase 6: 인덱싱 인프라 (백엔드만)

**목표:** 인메모리 인덱스 + SQLite 스냅샷 + 비권한 소스(재귀+FSW). UI 없음.

- [ ] 6.1 `IndexEntry` 컴팩트(부모 참조+이름, ~140B/파일) — **TDD**
- [ ] 6.2 `IFileIndex` (add/update/remove/query, 스레드 안전) — **TDD**
- [ ] 6.3 한글 부분 검색: NFC + lowercase shadow + substring — **TDD(한/영/혼합)**
- [ ] 6.4 SQLite 스키마 + 마이그레이션 — **TDD**
- [ ] 6.5 스냅샷 저장/로드(시작 하이드레이션) — **TDD**
- [ ] 6.6 단일 writer + WAL + 배치 트랜잭션 — **TDD + 동시성 테스트** (R-SQLITE)
- [ ] 6.7 `IIndexSource` 추상화 — **TDD**
- [ ] 6.8 재귀 열거 소스(진행·취소) — **TDD**
- [ ] 6.9 FSW 소스(델타 매핑, **오버플로 시 서브트리 재스캔** R-FSWLOSS) — **TDD**
- [ ] 6.10 `IHostedService` 백그라운드 인덱싱(초기 스캔→FSW 전환) — **TDD**

**DoD:** 스냅샷 콜드 스타트 · FSW 실시간 반영 · 한글 부분 매칭 정확 · 10만 파일 ≈ 14MB · 락 충돌 없음
**병렬:** **Phase 1~5와 완전 독립** — UI 트랙과 동시 진행 가능

---

## Phase 7: NTFS MFT/USN 고속 인덱싱 (권한 상승)

- [ ] 7.1 `Explorer.Helper.Elevated` exe + named pipe IPC 프로토콜 — **TDD(직렬화)**
- [ ] 7.2 MFT 열거(`FSCTL_ENUM_USN_DATA`, FRN→경로) — FRN 매핑 **TDD**, 통합 수동
- [ ] 7.3 USN tailing(레코드→델타, USN 위치 영속→재시작 replay) — 파싱 **TDD**
- [ ] 7.4 UAC 동의 흐름 + 거부 시 폴백 graceful degrade + 헬퍼 크래시 복구
- [ ] 7.5 소스 자동 선택(NTFS+권한→MFT/USN, 그 외→재귀+FSW) + 오버라이드 — **TDD**
- [ ] 7.6 (백로그) Windows 서비스 모드 상시 tailing

**DoD:** 권한 시 NTFS 전체 수 초~분 인덱싱 · USN 준실시간 · 거부 시 기능 저하만(R-ELEVATION) · 헬퍼 크래시 무영향

---

## Phase 8: 글로벌 검색 팝업 + 트레이

- [ ] 8.1 `IGlobalHotkeyService` — 기본 **Alt+Space**, 실패 감지·안내, double-Ctrl 프리셋, 재바인딩 — **TDD** (R-IMEHOTKEY)
- [ ] 8.2 트레이(H.NotifyIcon: 검색/설정/종료) + 자동 시작 — 등록 로직 TDD
- [ ] 8.3 중앙 팝업 창(topmost, ESC/포커스 아웃 숨김) — 수동
- [ ] 8.4 `SearchPopupViewModel` (디바운스→인덱스 쿼리→취소) — **TDD**
- [ ] 8.5 결과 목록(가상화·아이콘·경로) — 수동
- [ ] 8.6 Enter=실행 / Ctrl+Enter=페인 reveal — 라우팅 **TDD**
- [ ] 8.7 랭킹(정확>접두>부분, MRU 가중) — **TDD**

**DoD:** 어디서든 Alt+Space → as-you-type · Enter/Ctrl+Enter · 핫키 재바인딩 · 메인 창 닫아도 동작

---

## Phase 9: 미리보기 → ⭐ v1.0

- [ ] 9.1 `IPreviewRenderer` + 레지스트리(확장자 first-match) — **TDD**
- [ ] 9.2 이미지(EXIF 회전·다운스케일) · 9.3 텍스트/코드(AvalonEdit·인코딩 감지 TDD) · 9.4 미디어 · 9.5 압축 목록(**TDD**)
- [ ] 9.6 `HwndHost` IPreviewHandler 호스팅(CLSID 조회 TDD) — **PowerToys Peek(MIT) 참조** (QuickLook GPL 복사 금지)
- [ ] 9.7 핸들러 수명/리사이즈/크래시 격리(타임아웃+블랙리스트) (R-PREVIEWBITNESS)
- [ ] 9.8 `PreviewCoordinator` — 선택 변경 → **디바운스 250ms + 취소** — **TDD**
- [ ] 9.9 **반대 페인 미리보기 탭** (Ctrl+Q 토글, 원래 탭 복원) — 수동
- [ ] 9.10 Space 퀵 프리뷰 팝업(렌더러 공유) — 수동
- [ ] 9.11 불가/로딩/오류 상태 UI

**DoD:** Ctrl+Q 반대 페인 추적 미리보기 · Space 팝업 · 내장+핸들러 렌더링 · 핸들러 오류≠크래시 · 빠른 이동 시 취소로 잰크 없음

---

## Phase 10: 커스터마이즈

- [ ] 10.1 `ColorRule` + first-match 엔진 — **TDD** · 10.2 목록 적용 · 10.3 편집 UI(추가/순서/색)
- [ ] 10.4 테마(다크/라이트/시스템)·폰트·행 높이 + 설정 UI
- [ ] 10.5 `ColumnLayout` 모델 — **TDD** · 10.6 폴더별 뷰 기억 — **TDD**
- [ ] 10.7 퀵 필터(Ctrl+F type-to-filter) — 로직 **TDD**

**DoD:** 확장자 색 규칙 편집·즉시 반영 · 외관 영속 · 컬럼 폴더별 기억 · 퀵 필터
**병렬:** 4트랙 전부 독립

---

## Phase 11: 검색 고도화 + 네트워크 드라이브

- [ ] 11.1 쿼리 파서(`ext: size:> date: path: regex:` → AST) — **TDD 전수**
- [ ] 11.2 필터 평가 엔진 — **TDD**
- [ ] 11.3 팝업/필터 구문 통합 + 도움말 힌트
- [ ] 11.4 네트워크 소스(UNC, 인증·끊김 처리) — **TDD**
- [ ] 11.5 소스별 재스캔 스케줄(분/시/일/안 함, staggering·동시 수 제한) — **TDD** (R-NETSCANSTORM)
- [ ] 11.6 네트워크 FSW best-effort 폴백 결정 — **TDD**
- [ ] 11.7 드라이브별 opt-in 설정 UI

**DoD:** `ext:pdf size:>1mb` 복합 쿼리 · 네트워크 opt-in+주기 재스캔 · 끊김 시 graceful

---

## Phase 12: 유틸리티

- [ ] 12.1 배치 rename 엔진(`{name}{counter}{date}{ext}`·regex·충돌 검출) — **TDD** · 12.2 미리보기 표 UI
- [ ] 12.3 압축 폴더처럼 탐색(zip/7z/rar) — **TDD** · 12.4 압축 생성(zip/7z) · 12.5 해제 — **TDD+통합**
- [ ] 12.6 폴더 크기 온디맨드(백그라운드·취소) — **TDD**
- [ ] 12.7 중복 찾기(크기→해시 그룹핑) — **TDD**

**병렬:** 각 유틸 상호 독립

---

## Phase 13: 견고성 & 마무리

- [ ] 13.1 권한 거부 → 상승 재시도 흐름 — 결정 로직 **TDD**
- [ ] 13.2 Long-path(`\\?\`) 전 경로 검증 — **TDD** *(가능하면 Phase 2부터 PathUtils에 선반영 — R-LONGPATH)*
- [ ] 13.3 i18n 인프라(ko/en, 런타임 전환) — **TDD** · 13.4 하드코딩 문자열 제거
- [ ] 13.5 포터블 모드 — **TDD**
- [ ] 13.6 FlaUI 스모크(시작→탐색→복사→탭→검색) — **E2E**
- [ ] 13.7 첫 실행 온보딩(인덱싱 권한·핫키 안내)
- [ ] 13.8 설정 import/export + 진단 로그 내보내기

---

## 백로그 (post-v1.x)

폴더 비교/동기화 · out-of-proc 셸/미리보기 격리 정식화 · Windows 서비스 USN 상시 tailing · 태그 시스템 · 파일 내용 검색 · OneDrive placeholder · 플러그인 API · Velopack 자동 업데이트

## 의존성 그래프

```
Phase 0 ─→ 1 ─→ 2 ─┬─→ 4(v0.1) ─→ 5 ─────────→ 12
            └→ 3 ──┘      │
                          └→ 10
Phase 0 ─→ 6 ─→ 7 ─→ 8 ──→ 11        [6~8은 1~5와 완전 병렬 가능]
Phase 3 + 8 ─→ 9(v1.0)
전체 ─→ 13
```

## 위험 등록부 (요약)

| ID | 위험 | 완화 |
|----|------|------|
| R-SHELLCRASH | 서드파티 셸 확장 in-proc 크래시 | try/catch+메뉴 재생성+블랙리스트, 장기 out-of-proc |
| R-PREVIEWBITNESS | IPreviewHandler 불안정/비트니스 | 타임아웃+블랙리스트, PowerToys Peek 방식 격리 |
| R-ELEVATION | UAC 거부/거부감 | 헬퍼 1회 기동+IPC, **비권한 폴백 1급 시민** |
| R-IMEHOTKEY | Win+Space OS 예약, Alt+Space 충돌 가능 | Alt+Space 기본+실패 안내+double-Ctrl 프리셋 |
| R-NETSCANSTORM | 네트워크 재스캔 폭주 | staggering+동시 수 제한+백오프+기본 off |
| R-PERF | 10만+ 항목 잰크 | 가상화 강제, 조기 부하 테스트, 정렬은 백그라운드 |
| R-ICONJANK | 동기 아이콘 로딩 멈춤 | placeholder→비동기+LRU 캐시 |
| R-SQLITE | 쓰기 락 경합 | 단일 writer+WAL+배치 |
| R-FSWLOSS | FSW 버퍼 오버플로 유실 | 오버플로 감지→서브트리 재스캔 |
| R-TABMEM | 다수 탭 메모리 폭증 | 탭=경량 상태, VM 재사용(swap) |
| R-LONGPATH | long-path 후반 소급 비용 | PathUtils에 초기 반영 |
