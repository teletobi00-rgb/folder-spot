<#
.SYNOPSIS
  Folder Spot release build + Velopack packaging.

.DESCRIPTION
  Publishes the App and both helpers (Elevated/ShellMenu) self-contained into one folder.
  The three exes share the same .NET runtime DLLs, so only one runtime copy is included
  (1 runtime + 3 exes). Then vpk builds Setup.exe + delta packages; -Upload pushes a GitHub release.

.PARAMETER Version
  Release version (SemVer), e.g. 1.0.0

.PARAMETER Upload
  Upload to GitHub release (requires $env:GITHUB_TOKEN).

.EXAMPLE
  ./publish.ps1 -Version 1.0.0
  ./publish.ps1 -Version 1.0.1 -Upload
#>
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$pub = Join-Path $root "publish"
$releases = Join-Path $root "Releases"
$icon = Join-Path $root "src\Explorer.App\Assets\app.ico"
$repoUrl = "https://github.com/teletobi00-rgb/folder-spot"

Write-Host "== Folder Spot v$Version release build ($Configuration/$Runtime) ==" -ForegroundColor Cyan

# 1) Clean previous publish output
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }

# 2) Publish App + 2 helpers self-contained into the SAME folder (shared runtime DLLs)
$projects = @(
    "src\Explorer.App\Explorer.App.csproj",
    "src\Explorer.Helper.Elevated\Explorer.Helper.Elevated.csproj",
    "src\Explorer.Helper.ShellMenu\Explorer.Helper.ShellMenu.csproj"
)
foreach ($proj in $projects) {
    Write-Host "-- publish: $proj" -ForegroundColor DarkGray
    # ReadyToRun: precompile IL to native to cut JIT warmup / speed up startup.
    # -p:Version stamps the assembly so the app can show its release version at runtime.
    dotnet publish (Join-Path $root $proj) -c $Configuration -r $Runtime --self-contained true -p:PublishReadyToRun=true -p:Version=$Version -o $pub
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $proj" }
}

# 3) Velopack packaging (Setup.exe + full/delta nupkg + RELEASES)
Write-Host "-- vpk pack" -ForegroundColor DarkGray
vpk pack --packId FolderSpot --packVersion $Version --packDir $pub --mainExe "Explorer.App.exe" --packTitle "Folder Spot" --icon $icon --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

# 4) Optional: upload GitHub release
if ($Upload) {
    if (-not $env:GITHUB_TOKEN) { throw "Set GITHUB_TOKEN to upload." }
    Write-Host "-- vpk upload github ($repoUrl)" -ForegroundColor DarkGray
    vpk upload github --repoUrl $repoUrl --publish --releaseName "v$Version" --tag "v$Version" --token $env:GITHUB_TOKEN --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }
}

Write-Host "== Done: $releases ==" -ForegroundColor Green
Get-ChildItem $releases | Select-Object Name, @{N="MB";E={[math]::Round($_.Length/1MB,1)}}
