# 시스템 설계 — Explorer

> 서브시스템별 상세 설계. 아키텍처 전제는 [02_architecture.md](02_architecture.md) 참조.

## 1. 인덱서 (Explorer.Indexing)

Everything의 검증된 구조를 따른다: **인메모리 인덱스가 진실의 원천, SQLite는 빠른 시작용 스냅샷.**

### 1.1 인메모리 인덱스
- `IndexEntry`: 파일명 + 부모 디렉터리 **참조(ID)** 분리 저장(전체 경로 미보관), 문자열 인터닝 → 목표 ~140B/파일 (100만 파일 ≈ 140MB, Everything 실측 기반).
- 검색 컬럼: NFC 정규화 + `ToLowerInvariant()` **lowercase shadow** — SQLite `LIKE`는 ASCII만 대소문자 무시하므로 한글·유니코드는 shadow 컬럼이 정답.
- 부분(substring) 매칭: 인메모리 선형 스캔(100만 행 수십 ms 수준)이 1차. FTS5 trigram은 3자 이상 쿼리 가속용 **선택적 보조**(한글 1~2음절 쿼리는 어차피 폴백되므로 필수 아님).
- 동시성: 읽기-쓰기 분리(스냅샷 읽기 or ReaderWriterLock), 쓰기는 인덱싱 파이프라인 단일 진입.

### 1.2 인덱스 소스 (`IIndexSource` 추상화)

| 소스 | 대상 | 초기 구축 | 증분 갱신 | 비고 |
|------|------|----------|----------|------|
| **MFT/USN** (관리자) | 로컬 NTFS | `FSCTL_ENUM_USN_DATA` MFT 열거 (FRN→경로 해석) | `FSCTL_READ_USN_JOURNAL` tailing, USN 위치 영속 → 재시작 시 **놓친 변경 replay** | `Explorer.Helper.Elevated` 프로세스에서 실행, named pipe IPC |
| **재귀 열거 + FSW** (폴백) | NTFS 비권한 / FAT·exFAT | `System.IO.Enumeration` 재귀 스캔 | `FileSystemWatcher` 델타. 버퍼 오버플로 감지 시 해당 서브트리 재스캔 (R-FSWLOSS) | 권한 없이도 전 기능 동작하는 1급 경로 |
| **네트워크** (opt-in) | UNC/매핑 드라이브 | 재귀 스캔 | FSW best-effort(SMB 불안정) + **주기 재스캔**: 분/시간/일/안 함 | 소스별 스케줄 staggering + 동시 스캔 수 제한 (R-NETSCANSTORM) |

소스 선택은 자동(볼륨 타입+권한 검사) + 설정 오버라이드. 드라이브별 포함/제외 opt-in.

### 1.3 SQLite 스냅샷
```sql
CREATE TABLE files(
  id INTEGER PRIMARY KEY, parent_id INTEGER, name TEXT NOT NULL,
  name_lower TEXT NOT NULL,        -- NFC + lower shadow
  ext TEXT, size INTEGER, mtime INTEGER, attrs INTEGER, is_dir INTEGER
);
CREATE INDEX ix_files_name_lower ON files(name_lower);
CREATE INDEX ix_files_parent ON files(parent_id);
PRAGMA journal_mode=WAL;
```
- 시작: SQLite → 인메모리 하이드레이션 → USN replay → 검색 가능 (콜드 스타트 < 2s 목표).
- 쓰기: 단일 writer 스레드 + 배치 트랜잭션(예: 5천 건 or 2초 플러시). 읽기는 별도 커넥션.
- 스키마에 `meta(version)` 테이블 — 마이그레이션 대비.

## 2. 글로벌 검색 런처

- **핫키**: `RegisterHotKey` — 기본 **Alt+Space**. `Win+Space`는 OS가 IME 전환에 예약(가로채기 불가)이므로 미지원을 명시하고, 등록 실패 시(다른 앱 선점 등) 즉시 안내 + 대체 제안. 프리셋: Alt+Space / double-Ctrl(LL 훅 기반) / 사용자 정의.
- **팝업 UX** (PowerToys Run 관례): 모니터 중앙 topmost borderless, as-you-type(디바운스 ~50ms) + CancellationToken, ↑↓ 선택, `Enter`=실행, `Ctrl+Enter`=활성 페인에서 폴더 열고 항목 선택(reveal), `Esc`=숨김(종료 아님 — 즉시 재오픈).
- **랭킹**: 정확 일치 > 접두 일치 > 부분 일치 > 경로 일치, 동순위는 최근 사용(MRU 가중) → 수정일.
- **검색 문법** (Phase 11): `ext:pdf size:>10mb date:2026-01.. path:src regex:^IMG_\d+` → 파서가 AST 생성 → 인덱스 쿼리 + 후처리 필터. 잘못된 구문은 인라인 피드백.
- 메인 창과 무관하게 동작해야 하므로 트레이 상주(H.NotifyIcon) + 자동 시작 옵션.

## 3. 파일 작업 엔진

- **실행 계층**: Vanara `ShellFileOperations`(COM `IFileOperation`) — 휴지통 삭제(`AllowUndo`), 셸 진행 콜백, 탐색기와 동일한 시맨틱. HRESULT → 도메인 에러 매핑(권한 거부/사용 중/경로 초과 구분).
- **큐**: `IOperationQueue` 상태 머신 `Pending→Running⇄Paused→Done|Failed|Canceled`, 동시 실행 수 제한. 진행 모델: 바이트/항목 카운트, 이동평균 속도, ETA.
- **충돌**: 셸 다이얼로그 대신 **커스텀** — 소스/타겟 메타(크기·날짜·썸네일) 비교 표시, 덮어쓰기/건너뛰기/둘 다 유지 + "모두 적용" (Files 앱 패턴).
- **Undo**: 작업별 역연산 기록 — move→역move, rename→역rename, delete→휴지통 복원. Ctrl+Z 스택 + 작업 히스토리 로그 뷰.
- **클립보드**: `CF_HDROP` + `Preferred DropEffect`로 복사/잘라내기 구분 — 탐색기와 상호 호환.
- **D&D**: 앱→탐색기 `DataObject.SetFileDropList`(CF_HDROP), 탐색기→앱 `DataFormats.FileDrop`. 자기 자신/하위 폴더 드롭 차단 검증. (가상 파일 D&D — 압축 내부 항목 등 — 는 Files 앱 MIT 구현 참조, v1.x)
- **크로스 페인 시맨틱**: "타겟 = 반대 페인의 활성 탭 경로" 해석기를 단일 함수로 두고 F5/F6/D&D가 공유.

## 4. 미리보기 (Explorer.Preview)

- **렌더러 레지스트리**: `IPreviewRenderer` 확장자 매핑, first-match. 우선순위: 내장 렌더러 → IPreviewHandler → 폴백(파일 정보/hex).
  - 내장: 이미지(BitmapImage, EXIF 회전, 대용량 다운스케일) · 텍스트/코드(AvalonEdit, 인코딩 감지) · 미디어(MediaElement) · 압축(SharpCompress 목록 트리) · 마크다운(후순위)
  - **IPreviewHandler 호스팅**: `HwndHost` 자식 HWND → 레지스트리 `HKCR\.ext\shellex\{8895b1c6-…}` CLSID 조회 → `IInitializeWithFile/Stream` → `SetWindow`/`DoPreview`. **PowerToys Peek(MIT) 코드를 참조/포팅** (QuickLook은 GPL — 코드 복사 금지).
  - 안정성: 핸들러는 try/catch + 타임아웃 + 블랙리스트(설정 저장). 비트니스 불일치·크래시 잦으면 out-of-proc 호스트로 격리(백로그, R-PREVIEWBITNESS).
- **반대 페인 미리보기 탭** (F9, TC Ctrl+Q 패턴): Ctrl+Q 토글 → 반대 페인에 `IsPreviewTab` 탭 삽입(기존 탭 상태 보존) → 활성 페인 SelectionChanged를 **250ms 디바운스 + 취소 토큰**으로 추적 → 가벼운 정보는 즉시, 무거운 렌더는 디바운스 후. 재토글 시 원래 탭 복원.
- **Space 퀵 프리뷰**: 동일 레지스트리 공유, 오버레이 창, Space/Esc 닫기.

## 5. 셸 통합 (Explorer.Shell)

- **컨텍스트 메뉴**: Vanara `ShellContextMenu`(IContextMenu/2/3) — STA UI 스레드 + HWND 오너 + 메시지 펌프 필수. 자체 명령(복사/잘라내기/삭제/이름변경 등)과 병합. 서드파티 확장 크래시 대비 try/catch + 실패 시 메뉴 재생성 (R-SHELLCRASH).
- **아이콘/썸네일**: `IShellItemImageFactory`(Vanara ShellItemImages) — 비동기 전용 큐, 확장자 단위 캐시(exe/lnk/ico는 per-file), LRU 상한.
- **실행**: 더블클릭/Enter → `ShellExecute` (연결 프로그램, verb 지원).

## 6. 커스터마이즈 데이터 모델

```jsonc
// colorrules.json — 순서 = 우선순위, first-match-wins (TC wincmd.ini 모델의 JSON화)
{ "version": 1, "rules": [
  { "patterns": ["*.zip", "*.7z", "*.rar"], "color": "#E5C07B" },
  { "patterns": ["*.user", "*.suo", "thumbs.db", ".git*"], "color": "#5C6370" }
]}
// favorites.json — 파일/폴더 모두, kind로 구분
{ "version": 1, "items": [
  { "kind": "folder", "path": "D:\\Work", "label": "작업폴더", "order": 0 },
  { "kind": "file", "path": "D:\\메모.xlsx", "label": null, "order": 1 }
]}
```
- `ColumnLayout`(표시/순서/너비/정렬)은 기본 레이아웃 + 폴더별 오버라이드 사전. 탭 전환·재시작 시 복원.
- 외관: 테마(다크/라이트/시스템 추종), 폰트 패밀리/크기, 행 높이 — WPF UI `ApplicationTheme` + DynamicResource.

## 7. 키보드 계약 (기본 키맵)

| 키 | 동작 | | 키 | 동작 |
|----|------|-|----|------|
| F5 / F6 | 반대 페인으로 복사 / 이동 | | Ctrl+T / Ctrl+W | 탭 추가 / 닫기 |
| F2 | 이름 변경 (인라인) | | Ctrl+Tab | 다음 탭 |
| F7 (Ctrl+Shift+N) | 새 폴더 | | Tab | 페인 전환 |
| F8 / Del | 휴지통 삭제 (Shift+Del 영구) | | Ctrl+U | 페인 스왑 |
| Ctrl+C/X/V | 복사/잘라내기/붙여넣기 | | Ctrl+←/→ | 선택 폴더를 반대 페인에 열기 |
| Ctrl+F | 퀵 필터 | | Ctrl+Q | 반대 페인 미리보기 탭 토글 |
| Alt+←/→/↑ | 뒤로/앞으로/위로 | | Space | 퀵 프리뷰 팝업 |
| Ctrl+L | 주소창 포커스 | | Alt+Space (전역) | 검색 팝업 |
| Ctrl+Z | 작업 취소(Undo) | | Esc | 필터/팝업 닫기 |

전 바인딩은 `IKeyBindingService`(keymap.json) 중앙 관리 — 재바인딩 UI는 v1.x.
