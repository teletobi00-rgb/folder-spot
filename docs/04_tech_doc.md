# 기술 문서 — Explorer

> 스택 결정 근거, 라이브러리 목록(라이선스 포함), 빌드/배포, 테스트 전략. 리서치 일자: 2026-06-12 (gh/웹 검증 완료)

## 1. 스택 결정: .NET 10 (LTS) + WPF

> 계획 시점엔 .NET 9였으나 .NET 9(STS)는 2026-05 지원 종료 → 구현 시작 시 **.NET 10 LTS(지원 ~2028-11)로 상향**. WPF UI 4.3이 net10 타겟을 제공해 변경 비용 없음.

| 후보 | 평가 | 결론 |
|------|------|------|
| **C# WPF (.NET 10)** | 셸 통합(IContextMenu·IFileOperation·IPreviewHandler·휴지통·아이콘) 최저 마찰, HwndHost 성숙, 가상화 검증됨, WPF UI로 Fluent 룩, 단일 exe 배포 | ✅ **채택** |
| C# WinUI 3 | 가장 모던한 룩(Files 앱 스택)이나 도구 성숙도 낮고 HwndHost 부재(미리보기 핸들러 호스팅 우회 필요) | ❌ |
| Tauri 2 (Rust+React) | 인덱서 성능 우수하나 셸 메뉴·미리보기·네이티브 D&D를 Rust FFI로 전부 자작 — 핵심 기능 난이도 급상승 | ❌ |
| Electron | 메모리·대용량 목록·셸 통합 한계, 파일매니저 부적합 | ❌ |

개발 환경: .NET SDK 10.0.301 설치 완료 (winget). 솔루션 파일은 SDK 10 기본 포맷인 `Explorer.slnx`.

## 2. 라이브러리 (검증 완료, 2026-06 기준)

| 용도 | 라이브러리 | 라이선스 | 판정 | 비고 |
|------|-----------|---------|------|------|
| Fluent 테마/컨트롤 | **lepoco/wpfui** v4.x | MIT | 채택 | 활발(2026-06 커밋), net9.0-windows 타겟. 테마+컨트롤 선택 사용(NavigationView는 opinionated) |
| MVVM | **CommunityToolkit.Mvvm** 8.4 | MIT | 채택 | `[ObservableProperty]`/`[RelayCommand]` 소스 생성 |
| 셸 interop | **Vanara** 5.x (`Vanara.Windows.Shell.Common`, `Vanara.PInvoke.Shell32`) | MIT | 채택 | `ShellContextMenu`(IContextMenu 호스팅), `ShellFileOperations`(IFileOperation+휴지통+진행), `ShellItemImages`(아이콘/썸네일) 검증됨 |
| DI/호스팅/로깅 | Microsoft.Extensions.Hosting + **Serilog** | MIT/Apache | 채택 | IHostedService로 인덱서 수명 관리 |
| 인덱스 DB | **Microsoft.Data.Sqlite** 9.x | MIT | 채택 | 번들 e_sqlite3에 **FTS5 포함 확인됨**. 단 한글 substring은 lowercase shadow + LIKE가 정답 (FTS5 trigram은 3자+ 보조) |
| 텍스트/코드 미리보기 | **AvalonEdit** 6.3 | MIT | 채택 | xshd 문법 구식 → 필요 시 TextMateSharp 보강 |
| 압축 | **SharpCompress** 0.49 | MIT | 채택 | zip/7z/rar(rar5) 읽기 + zip/**7z** 쓰기. 순수 관리코드 |
| 트레이 | **H.NotifyIcon** 2.4 | MIT | 채택 | hardcodet의 활발한 후속 |
| 인앱 D&D | gong-wpf-dragdrop 4.0 | BSD-3 | 선택 | 탭 재정렬·즐겨찾기용. 셸 D&D는 자작(CF_HDROP) |
| 테스트 | xUnit + FluentAssertions + NSubstitute + FlaUI | MIT 등 | 채택 | FlaUI는 후반 스모크용 |

### 참조 전용 (코드 복사 여부 주의)

| 자료 | 라이선스 | 용도 |
|------|---------|------|
| **microsoft/PowerToys — Peek 모듈** | MIT | ✅ IPreviewHandler 호스팅 코드 **포팅 가능** — 미리보기의 1차 참조 |
| **files-community/Files** | MIT | ✅ 셸 작업 레이어·충돌 다이얼로그·가상 파일 D&D 패턴 참조/포팅 가능 |
| **wangfu91/UsnParser** | MIT | ✅ `FSCTL_ENUM_USN_DATA`/`READ_USN_JOURNAL` C# 패턴 복사 가능 |
| **DearVa/ExplorerEx** | MIT | ✅ WPF 멀티탭 탐색기 — 셸 D&D·탭 코드 참조 (아카이브됨) |
| **sgrottel/EverythingSearchClient** | Apache-2.0 | (선택) Everything 설치 시 외부 인덱스 제공자 옵션 |
| **QL-Win/QuickLook** | **GPL-3.0** | ⚠️ **코드 복사 금지** — 아이디어/동작 참조만 |
| NtfsReader 계열 | LGPL | ⚠️ 소스 복사 시 LGPL 의무 — 패턴 참조만 |
| voidtools Everything | 비공개(SDK 무료) | 설계 참조(MFT+USN+인메모리, 폴더 인덱싱 재스캔 UX) |

**라이선스 정책: 본 프로젝트에 포함 가능한 코드는 MIT/BSD/Apache만. GPL/LGPL은 참조만.**

## 3. 핵심 기술 검증 메모

- **Win+Space 불가**: Windows 10/11이 입력 언어 전환에 시스템 예약 — RegisterHotKey 선점 불가, LL 훅으로 빼앗는 건 IME 사용자(한국어!) 환경 파괴라 배제. PowerToys Run·Flow Launcher 기본값이 Alt+Space인 이유. → 기본 Alt+Space + double-Ctrl 프리셋 + 재바인딩.
- **MFT/USN은 관리자 필요**: 볼륨 핸들 raw 접근. Everything도 서비스로 해결 → 우리는 UAC 헬퍼 프로세스(1회 동의) + **비권한 폴백을 1급 시민**으로.
- **SQLite LIKE는 ASCII만 case-insensitive** → 한글 검색은 NFC 정규화 + lowercase shadow 컬럼 필수.
- **IPreviewHandler는 x64 일치 필요 + Office/Adobe 핸들러 불안정** → 타임아웃+블랙리스트, 장기 out-of-proc 격리.
- **WPF DataGrid는 컬럼 가상화 이슈 보유** → `ListView+GridView`(row 가상화 Recycling) 우선 검토, 10만 항목 조기 부하 테스트.

## 4. 빌드 / 배포

- `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` — 단일 exe (SDK 없는 PC 동작). Helper exe는 별도 산출물로 동봉.
- Trimming은 WPF 미지원 → 사이즈는 ReadyToRun 정도만.
- 자동 업데이트(v1.x): Velopack 검토.
- CI: GitHub Actions `windows-latest` — build + test. 커밋 컨벤션: `feat:/fix:/refactor:/docs:/test:/chore:` (사용자 git 룰).

## 5. 테스트 전략 (사용자 룰: TDD, 비-UI 80%+)

| 레이어 | 방법 | 커버리지 목표 |
|--------|------|--------------|
| Core (모델·정렬·경로·히스토리·규칙엔진·파서) | xUnit 순수 단위 — **RED→GREEN→REFACTOR** | 90%+ |
| Indexing (인덱스·스냅샷·델타·스케줄) | 단위 + temp dir/in-memory SQLite 통합 | 85%+ |
| Shell/Preview | 인터페이스 계약 단위 + temp dir 통합(파일작업), COM 호스팅은 수동 | 로직 부분 80% |
| ViewModel | mock 주입(NSubstitute) 단위 | 80%+ |
| View/UX | 수동 체크리스트 + FlaUI 스모크(Phase 13) | 핵심 플로우 |

수동 검증 항목(가상화 성능, 셸 메뉴, D&D, UAC, 핸들러 호스팅)은 각 Phase DoD에 명시 — [05_task_list.md](05_task_list.md).
