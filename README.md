# Folder Spot

Windows용 듀얼 페인 파일 탐색기 (.NET 10 · WPF). Total Commander 스타일의 2분할·탭 탐색에, Everything 방식의 즉시 검색(Alt+Space)과 셸 통합을 더했다.

## 주요 기능

- **듀얼 페인 + 페인별 탭**, 크로스 페인 복사/이동(F5/F6), 즐겨찾기, 중앙 키맵
- **전역 즉시 검색** (Alt+Space): 인메모리 인덱스 기반 파일 검색 + **설치된 프로그램/앱**(Win32·UWP) 통합, MRU 가중
- **상단 빠른 실행 바**: 자주 쓰는 프로그램 런처(드래그&드롭 추가)
- **미리보기**: 이미지/텍스트/미디어/압축 + Office·PDF 실제 OLE(IPreviewHandler) 미리보기
- **파일 작업**: 셸 네이티브 진행/충돌 UI, 휴지통, 작업 큐, Undo
- **셸 통합**: 네이티브 컨텍스트 메뉴를 별도 헬퍼 프로세스에서 호스팅해 서드파티 확장 크래시를 격리
- **편의**: 일괄 이름 변경, 폴더 크기 계산, 압축/해제, 두 페인 비교, 썸네일 보기, 여기서 터미널 열기
- **표시**: AIP/암호 보호 Office 파일(🔒), 확장자별 글자색 커스텀, 다크/라이트 테마
- **인덱싱**: 비권한 재귀+FSW 기본, NTFS MFT/USN 고속 인덱싱 옵트인. 정크 트리(WinSxS·node_modules·캐시 등) 제외로 가벼운 메모리
- **트레이 상주** + Windows 시작 시 자동 실행 옵션

## 빌드

```powershell
dotnet build Explorer.slnx        # 솔루션은 .slnx (SDK 10 포맷)
dotnet test  Explorer.slnx        # 테스트
```

> 스택: .NET 10 · WPF · WPF-UI · CommunityToolkit.Mvvm · Vanara · Microsoft.Data.Sqlite · AvalonEdit · SharpCompress · H.NotifyIcon · Velopack

## 배포 / 자동 업데이트

Velopack으로 패키징하고 GitHub 릴리스로 자동 업데이트한다. [docs/RELEASING.md](docs/RELEASING.md) 참고.

```powershell
./publish.ps1 -Version 1.0.0           # Setup.exe + 패키지 생성
./publish.ps1 -Version 1.0.1 -Upload   # GitHub 릴리스 업로드 (GITHUB_TOKEN 필요)
```

## 라이선스

내부/개인 프로젝트. 번들 폰트 Pretendard(SIL OFL 1.1).
