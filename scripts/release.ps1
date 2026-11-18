<#
.SYNOPSIS
  Builds DualSense Studio and publishes it as a GitHub Release that installed copies auto-update from.

.EXAMPLE
  .\scripts\release.ps1                 # bumps the patch version (0.1.0 -> 0.1.1) and publishes
  .\scripts\release.ps1 -Version 0.2.0  # publishes an explicit version
  .\scripts\release.ps1 -NoUpload       # build + package locally only (output in .\releases)

.NOTES
  Requires: .NET 10 SDK, the GitHub CLI logged in (gh auth login), and the Velopack CLI
  (installed automatically: dotnet tool install -g vpk).
#>
param(
    [string]$Version,
    [string]$Notes = "",
    [switch]$NoUpload
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$VpkVersion = '1.2.161'          # keep in sync with the Velopack package in DualSenseStudio.App.csproj
$PackId     = 'DualSenseStudio'
$RepoUrl    = 'https://github.com/JackLongman12/dualsense-studio'
$VersionFile = Join-Path $root 'version.txt'
$PublishDir  = Join-Path $root 'artifacts\publish'
$ReleaseDir  = Join-Path $root 'releases'

# --- version -------------------------------------------------------------------
if (-not $Version) {
    $current = if (Test-Path $VersionFile) { (Get-Content $VersionFile -Raw).Trim() } else { '0.0.0' }
    $parts = $current.Split('.')
    $Version = "{0}.{1}.{2}" -f $parts[0], $parts[1], ([int]$parts[2] + 1)
}
Write-Host "==> Releasing DualSense Studio v$Version" -ForegroundColor Cyan

# --- tools ---------------------------------------------------------------------
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$toolList = dotnet tool list -g
if (-not ($toolList | Select-String -Pattern "^vpk\s+$([regex]::Escape($VpkVersion))")) {
    Write-Host "==> Installing Velopack CLI (vpk $VpkVersion)"
    if ($toolList | Select-String -Pattern '^vpk\s') { dotnet tool uninstall -g vpk | Out-Null }
    dotnet tool install -g vpk --version $VpkVersion | Out-Null
}
$env:Path += ";$env:USERPROFILE\.dotnet\tools"

$token = $null
if (-not $NoUpload) {
    $token = (gh auth token).Trim()
    if (-not $token) { throw "GitHub CLI is not logged in. Run: gh auth login" }
}

# --- build ---------------------------------------------------------------------
Write-Host "==> Publishing (self-contained win-x64)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish src\DualSenseStudio.App\DualSenseStudio.App.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=false -o $PublishDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# --- package -------------------------------------------------------------------
New-Item -ItemType Directory -Force $ReleaseDir | Out-Null
if (-not $NoUpload) {
    Write-Host "==> Fetching previous release (for delta updates)"
    vpk download github --repoUrl $RepoUrl --token $token --outputDir $ReleaseDir --pre 2>$null
}

Write-Host "==> Packing with Velopack"
$notesFile = Join-Path $env:TEMP "dss-notes-$Version.md"
Set-Content -Path $notesFile -Value $(if ($Notes) { $Notes } else { "Test build $Version" })
vpk pack --packId $PackId --packVersion $Version --packDir $PublishDir --mainExe "$PackId.exe" --runtime win-x64 `
    --packTitle "DualSense Studio" --icon src\DualSenseStudio.App\Assets\app.ico --outputDir $ReleaseDir `
    --releaseNotes $notesFile
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Set-Content -Path $VersionFile -Value $Version -NoNewline

if ($NoUpload) {
    Write-Host "==> Done (local only). Installer: $ReleaseDir\$PackId-win-Setup.exe" -ForegroundColor Green
    return
}

# --- upload --------------------------------------------------------------------
Write-Host "==> Uploading GitHub release v$Version"
vpk upload github --repoUrl $RepoUrl --token $token --outputDir $ReleaseDir `
    --publish --releaseName "DualSense Studio $Version" --tag "v$Version" --merge
if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

Write-Host "==> Released v$Version. Installed copies will pick it up automatically." -ForegroundColor Green
Write-Host "    First-time install: download $PackId-win-Setup.exe from $RepoUrl/releases/latest"
