# 릴리스 / 자동 업데이트 가이드

Folder Spot는 **Velopack**으로 패키징하고 **GitHub 릴리스**로 배포·자동 업데이트한다.

## 구성 요약

- **패키징**: `publish.ps1` — App + 헬퍼 2개(Elevated/ShellMenu)를 같은 폴더에 **자체 포함(self-contained, win-x64)** 게시한다. 세 exe가 동일 버전 .NET 런타임 DLL을 공유하므로 런타임은 한 벌만 들어간다(폴더 ~196MB → 압축 ~90MB).
- **인스톨러**: `vpk pack`이 `FolderSpot-win-Setup.exe`(관리자 불필요, `%LocalAppData%\FolderSpot`에 설치) + `*-full.nupkg`(업데이트 패키지) + `Portable.zip` + `RELEASES` 메타를 만든다.
- **자동 업데이트**: 앱이 시작 8초 뒤 백그라운드에서 GitHub 릴리스를 확인(`UpdateService`). 새 버전이 있으면 받아두고 **트레이에 “⬆ 업데이트 설치 후 재시작”** 항목 + 풍선 알림을 띄운다. 사용자가 누르면 적용 후 재시작.
  - 개발/포터블 실행(미설치)에선 업데이트 확인이 **no-op**다(`UpdateManager.IsInstalled == false`).
- **리포지토리**: 공개 리포 `https://github.com/teletobi00-rgb/folder-spot`. 인증 토큰 없이 릴리스를 읽는다. URL은 `src/Explorer.App/Services/UpdateService.cs`와 `publish.ps1` 두 곳에 있다(리포명이 바뀌면 둘 다 수정).

## 사전 준비(1회)

```powershell
dotnet tool install -g vpk --version 1.2.0   # Velopack CLI (라이브러리와 버전 일치)
```

## 새 버전 릴리스

1. 코드 머지 후 버전을 정한다(SemVer, 예: `1.0.1`).
2. 로컬 패키징 검증:
   ```powershell
   ./publish.ps1 -Version 1.0.1
   ```
   `Releases\`에 Setup.exe/nupkg가 생기는지 확인.
3. GitHub 릴리스 업로드:
   ```powershell
   $env:GITHUB_TOKEN = "<repo 쓰기 권한 토큰>"
   ./publish.ps1 -Version 1.0.1 -Upload
   ```
   `vpk upload github`가 태그 `v1.0.1`로 릴리스를 만들고 자산을 올린다. 기존 설치본은 다음 확인 때 자동으로 1.0.1을 받는다.

## 주의

- **버전은 항상 올려야** 업데이트가 인식된다(현재 설치본 < 릴리스 버전).
- `publish.ps1`은 **ASCII(영문)로만** 작성한다 — Windows PowerShell 5.1이 BOM 없는 UTF-8 .ps1을 ANSI로 읽어 한글이 깨지면 파싱이 실패한다.
- MSIX는 셸 COM 통합·UAC 헬퍼·전역 핫키·HKCU 자동시작과 충돌해 채택하지 않는다.
- `publish/`, `Releases/`는 `.gitignore` 대상(빌드 산출물).
