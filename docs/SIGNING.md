# 코드 서명 (SignPath OSS) 설정 가이드

배포 exe를 코드 서명하면 Windows SmartScreen / 브라우저의 "안전하지 않은 파일" 경고가 사라진다.
공개 오픈소스라 **SignPath Foundation**의 무료 인증서를 쓴다. SignPath는 **CI(GitHub Actions)에서
빌드된 아티팩트만 서명**하므로, 릴리스는 `v*` 태그 푸시 → `.github/workflows/release.yml`에서 진행한다.

## 전제 (완료됨)

- [x] 공개 GitHub 리포 + **OSI 라이선스(MIT, `LICENSE`)**
- [x] CI 릴리스 워크플로 `.github/workflows/release.yml` (SignPath 시크릿이 있으면 자동 서명)

## 1단계 — SignPath Foundation 신청 (사용자)

1. https://signpath.org/ → "Apply now" (무료 OSS 프로그램). 리포 URL(`https://github.com/teletobi00-rgb/folder-spot`)과 프로젝트 설명 제출.
2. 승인되면 `app.signpath.io`에 조직(organization)이 만들어진다. 로그인.
   - 심사가 있고 며칠~몇 주 걸릴 수 있다. 신생/소규모 프로젝트는 보강 설명이 필요할 수 있다.

## 2단계 — SignPath 대시보드 구성 (사용자, 승인 후)

`app.signpath.io`에서:

1. **Trusted Build System** 연결: GitHub Actions(이 리포)를 OIDC로 신뢰 빌드 시스템으로 등록.
2. **Project** 생성 (예: `folder-spot`). → `프로젝트 슬러그` 확보.
3. **Artifact Configurations** 2개 생성:
   - `binaries` — 업로드한 `publish` zip 안의 실행 파일 서명. 예시 설정:
     ```xml
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip>
         <pe-file path="Explorer.App.exe"><authenticode-sign/></pe-file>
         <pe-file path="Explorer.Helper.Elevated.exe"><authenticode-sign/></pe-file>
         <pe-file path="Explorer.Helper.ShellMenu.exe"><authenticode-sign/></pe-file>
       </zip>
     </artifact-configuration>
     ```
   - `installer` — `Releases` zip 안의 `*Setup.exe` 서명:
     ```xml
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip>
         <pe-file path="FolderSpot-win-Setup.exe"><authenticode-sign/></pe-file>
       </zip>
     </artifact-configuration>
     ```
   - 각 슬러그 확보. (정확한 스키마는 SignPath 문서 참고 — 버전에 따라 태그명이 다를 수 있음.)
4. **Signing Policy** 생성 (예: `release-signing`). 승인 모드 선택(신뢰 CI는 자동 승인 가능, 또는 릴리스마다 수동 승인). → `정책 슬러그` 확보.
5. **API Token** 발급(CI 사용자) + **Organization ID** 확인.

## 3단계 — GitHub 리포 설정 (사용자)

리포 **Settings → Secrets and variables → Actions**:

- **Secrets**:
  - `SIGNPATH_API_TOKEN` = 2단계의 API 토큰
- **Variables**:
  - `SIGNPATH_ORG_ID` = Organization ID
  - `SIGNPATH_PROJECT_SLUG` = 프로젝트 슬러그
  - `SIGNPATH_POLICY_SLUG` = 정책 슬러그
  - `SIGNPATH_ARTIFACT_SLUG_BINARIES` = binaries 아티팩트 설정 슬러그
  - `SIGNPATH_ARTIFACT_SLUG_INSTALLER` = installer 아티팩트 설정 슬러그

> 시크릿이 없으면 워크플로는 **미서명 릴리스**를 만든다(파이프라인은 동일). 시크릿을 채우는 순간부터 서명된다.

## 4단계 — 릴리스 발행

```powershell
git tag v1.0.3
git push origin v1.0.3
```

→ GitHub Actions가 빌드 → (서명) → 릴리스 발행. Actions 탭에서 진행 확인.
(로컬 `publish.ps1 -Upload`는 미서명 수동 빌드용으로 남겨둔다 — 서명 릴리스는 CI로.)

## 참고

- SmartScreen "평판"은 서명 인증서 단위로 쌓인다. 서명 직후에도 초기엔 경고가 남을 수 있으나 다운로드가 쌓이면 사라진다(Foundation 인증서는 이미 평판이 있어 대개 즉시 깨끗함).
- 서명은 Setup.exe + 내부 앱 exe 모두에 적용되어 설치·실행·자동 업데이트가 모두 매끄러워진다.
- 완전 자동 서명이 어려우면 대안: **Azure Trusted Signing**(월 ~$10, 오픈소스 불필요) — 이 경우 `vpk pack`에 Azure 서명 옵션을 넣는다.
