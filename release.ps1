# TDS Pro Release Script
# Usage: .\release.ps1 -Version 1.0.1 -Notes "Bug fixes and improvements"
# This script bumps version in AppConstants.cs, TDSPro.App.csproj and CHANGELOG.txt together.
# Then builds, packages with Velopack and uploads to GitHub Releases.

param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Notes
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Step($msg) { Write-Host "`n== $msg ==" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "  OK: $msg" -ForegroundColor Green }
function Fail($msg) { Write-Host "  FAIL: $msg" -ForegroundColor Red; exit 1 }

# ── Validate version format ──────────────────────────────────────────────────
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "Version must be X.Y.Z (e.g. 1.0.1)" }

Step "Checking prerequisites"
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) { Fail "vpk not found. Run: dotnet tool install -g vpk" }
if (-not (Get-Command gh  -ErrorAction SilentlyContinue)) { Fail "gh not found. Install GitHub CLI." }
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Fail "Not logged in to GitHub. Run: gh auth login" }
Ok "Prerequisites OK"

# ── 1. Bump AppConstants.cs ──────────────────────────────────────────────────
Step "Bumping AppConstants.cs"
$constantsPath = Join-Path $root "TDSPro.Common\AppConstants.cs"
$constants = Get-Content $constantsPath -Raw
if ($constants -notmatch 'AppVersion\s*=\s*"[\d.]+"') { Fail "AppVersion not found in AppConstants.cs" }
$oldVersion = [regex]::Match($constants, 'AppVersion\s*=\s*"([\d.]+)"').Groups[1].Value
$constants = $constants -replace "AppVersion\s*=\s*""[\d.]+""", "AppVersion = ""$Version"""
Set-Content $constantsPath $constants -Encoding utf8
Ok "AppConstants.cs: $oldVersion -> $Version"

# ── 2. Bump TDSPro.App.csproj ────────────────────────────────────────────────
Step "Bumping TDSPro.App.csproj"
$csprojPath = Join-Path $root "TDSPro.App\TDSPro.App.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace "<Version>[\d.]+</Version>",         "<Version>$Version</Version>"
$csproj = $csproj -replace "<AssemblyVersion>[\d.]+</AssemblyVersion>", "<AssemblyVersion>$Version.0</AssemblyVersion>"
$csproj = $csproj -replace "<FileVersion>[\d.]+</FileVersion>",  "<FileVersion>$Version.0</FileVersion>"
Set-Content $csprojPath $csproj -Encoding utf8
Ok "TDSPro.App.csproj: $Version"

# ── 3. Prepend CHANGELOG.txt ─────────────────────────────────────────────────
Step "Updating CHANGELOG.txt"
$changelogPath = Join-Path $root "CHANGELOG.txt"
$existing = Get-Content $changelogPath -Raw
$date = Get-Date -Format "MMMM yyyy"
$newEntry = @"
v$Version ($date) — $Notes
$('─' * ($Version.Length + $date.Length + $Notes.Length + 10))
$Notes

"@
# Insert after the header (first 3 lines: title, ===, blank)
$lines = Get-Content $changelogPath
$header = $lines[0..2] -join "`n"
$rest   = $lines[3..($lines.Length-1)] -join "`n"
Set-Content $changelogPath "$header`n`n$newEntry$rest" -Encoding utf8
Ok "CHANGELOG.txt prepended with v$Version"

# ── 4. Build ─────────────────────────────────────────────────────────────────
Step "Building"
dotnet build "$root\TDSPro.App\TDSPro.App.csproj" -c Debug --nologo 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { Fail "Build failed" }
Ok "Build passed"

# ── 5. Test ──────────────────────────────────────────────────────────────────
Step "Running tests"
dotnet test "$root\TDSPro.Tests\TDSPro.Tests.csproj" --nologo 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { Fail "Tests failed — release aborted" }
Ok "All tests passed"

# ── 6. Publish ───────────────────────────────────────────────────────────────
Step "Publishing"
$publishDir = Join-Path $root "publish_velopack"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish "$root\TDSPro.App\TDSPro.App.csproj" -c Release -r win-x64 --self-contained -o $publishDir --nologo 2>&1 | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { Fail "Publish failed" }
Ok "Published to $publishDir"

# ── 7. Velopack pack ─────────────────────────────────────────────────────────
Step "Packaging with Velopack"
$releaseDir = Join-Path $root "releases_velopack"
vpk pack -u TDSPro -v $Version -p $publishDir --packTitle "TDS Pro" -e TDSPro.exe -o $releaseDir
if ($LASTEXITCODE -ne 0) { Fail "vpk pack failed" }
Ok "Package created"

# ── 8. Upload to GitHub ──────────────────────────────────────────────────────
Step "Uploading to GitHub Releases"
$token = (gh auth token)
vpk upload github -o $releaseDir --repoUrl https://github.com/arjun-panda/tdspro-releases --token $token
if ($LASTEXITCODE -ne 0) { Fail "vpk upload failed" }

# Publish the draft release
gh release edit $Version --repo arjun-panda/tdspro-releases --draft=false 2>&1
Ok "Release v$Version published at https://github.com/arjun-panda/tdspro-releases/releases/tag/$Version"

# ── 9. Git commit & push ─────────────────────────────────────────────────────
Step "Committing version bump"
git -C $root add "TDSPro.Common\AppConstants.cs" "TDSPro.App\TDSPro.App.csproj" "CHANGELOG.txt"
git -C $root -c user.name="Arjun Panda" -c user.email="arjunpanda@gmail.com" `
    commit -m "Release v$Version — $Notes"
git -C $root push origin main
Ok "Pushed to git"

Write-Host "`nRelease v$Version complete!" -ForegroundColor Green
