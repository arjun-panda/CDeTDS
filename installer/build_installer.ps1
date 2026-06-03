# CDeTDS — Installer Build Script
# Usage: .\installer\build_installer.ps1
# Requires: Inno Setup 6 installed at default path

$ErrorActionPreference = "Stop"
$root    = Split-Path $PSScriptRoot -Parent
$iscc    = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$appDir  = Join-Path $PSScriptRoot "AppFiles"
$pubDir  = Join-Path $PSScriptRoot "AppFiles\publish"

Write-Host "=== CDeTDS Installer Build ===" -ForegroundColor Cyan

# 1. Publish
Write-Host "Publishing Release build..." -ForegroundColor Yellow
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
& dotnet publish "$root\CDeTDS.App\CDeTDS.App.csproj" -c Release -r win-x64 --self-contained true --output $appDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 2. Remove nested publish subdir created by dotnet publish
if (Test-Path $pubDir) { Remove-Item $pubDir -Recurse -Force }

# 3. Run Inno Setup
Write-Host "Compiling installer..." -ForegroundColor Yellow
& $iscc "$PSScriptRoot\TDSPro_Setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed" }

$exe = Get-ChildItem "$PSScriptRoot\Output\CDeTDS_Setup_v*.exe" | Select-Object -Last 1
Write-Host "Done: $($exe.Name)  ($([math]::Round($exe.Length/1MB,1)) MB)" -ForegroundColor Green
