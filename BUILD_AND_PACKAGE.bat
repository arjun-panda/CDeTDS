@echo off
REM ============================================================
REM  TDS Pro v3.1.0 — MASTER BUILD + PACKAGE SCRIPT
REM
REM  Usage:
REM    BUILD_AND_PACKAGE.bat          → Full release build (slow, ~10 min)
REM    BUILD_AND_PACKAGE.bat FAST     → Dev build, no compression (fast, ~1 min)
REM
REM  Prerequisites:
REM    - .NET 8 SDK   : https://dotnet.microsoft.com/download/dotnet/8.0
REM    - Inno Setup 6 : https://jrsoftware.org/isinfo.php (for installer only)
REM ============================================================

setlocal enabledelayedexpansion

set APP_NAME=TDS Pro
set APP_VERSION=3.1.0
set PUBLISH_DIR=publish\win-x64
set INNO_COMPILER="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set ISS_FILE=TDSPro_Installer.iss

REM ── Build mode ───────────────────────────────────────────────
set FAST_BUILD=0
if /I "%1"=="FAST" set FAST_BUILD=1

echo.
echo  ============================================================
echo   %APP_NAME% v%APP_VERSION% — Master Build Script
if "%FAST_BUILD%"=="1" (
  echo   MODE: FAST DEV BUILD  (no compression, no ReadyToRun)
) else (
  echo   MODE: FULL RELEASE BUILD
)
echo  ============================================================
echo.

echo  Step 1/5 — Restoring NuGet packages...
echo  ────────────────────────────────────────
dotnet restore TDSPro.sln --nologo -r win-x64
if %ERRORLEVEL% NEQ 0 ( echo  ERROR: Restore failed & pause & exit /b 1 )
echo  Restore OK.
echo.

echo  Step 2/5 — Release build (all projects)...
echo  ────────────────────────────────────────────
dotnet build TDSPro.sln -c Release --nologo -v q --no-restore
if %ERRORLEVEL% NEQ 0 ( echo  ERROR: Build failed & pause & exit /b 1 )
echo  Build OK — 0 errors.
echo.

echo  Step 3/5 — Publish self-contained win-x64 single EXE...
echo  ──────────────────────────────────────────────────────────

REM ── Kill any running instance so the output EXE isn't locked ─────────────
taskkill /F /IM TDSPro.exe >nul 2>&1
timeout /t 3 /nobreak >nul
taskkill /F /IM TDSPro.exe >nul 2>&1
timeout /t 3 /nobreak >nul
taskkill /F /IM TDSPro.exe >nul 2>&1
timeout /t 2 /nobreak >nul

REM Wait until process is gone
:WAIT_LOOP
tasklist /FI "IMAGENAME eq TDSPro.exe" 2>nul | find /I "TDSPro.exe" >nul
if not errorlevel 1 (
    echo  Waiting for TDSPro.exe to close...
    timeout /t 1 /nobreak >nul
    goto WAIT_LOOP
)
REM Extra buffer for file handle release
timeout /t 5 /nobreak >nul

REM Force-delete the old EXE directly so it cannot be locked
if exist "%PUBLISH_DIR%\TDSPro.exe" (
    del /F /Q "%PUBLISH_DIR%\TDSPro.exe" >nul 2>&1
    timeout /t 2 /nobreak >nul
)

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

if "%FAST_BUILD%"=="1" (
    REM ── FAST: skip compression and ReadyToRun — ~60 seconds ──
    dotnet publish TDSPro.App\TDSPro.App.csproj ^
        -c Release ^
        -r win-x64 ^
        --self-contained true ^
        -p:PublishSingleFile=true ^
        -p:EnableCompressionInSingleFile=false ^
        -p:PublishReadyToRun=false ^
        -p:IncludeNativeLibrariesForSelfExtract=false ^
        -p:DebugType=none ^
        -p:DebugSymbols=false ^
        -o %PUBLISH_DIR% ^
        --nologo --no-restore
) else (
    REM ── FULL: compression, no ReadyToRun (requires runtime pack) — ~3-5 min
    dotnet publish TDSPro.App\TDSPro.App.csproj ^
        -c Release ^
        -r win-x64 ^
        --self-contained true ^
        -p:PublishSingleFile=true ^
        -p:IncludeNativeLibrariesForSelfExtract=true ^
        -p:EnableCompressionInSingleFile=true ^
        -p:PublishReadyToRun=false ^
        -p:DebugType=none ^
        -p:DebugSymbols=false ^
        -o %PUBLISH_DIR% ^
        --nologo --no-restore
)

if %ERRORLEVEL% NEQ 0 ( echo  ERROR: Publish failed & pause & exit /b 1 )
echo  Publish OK.
echo.

echo  Step 4/5 — Copying support files...
echo  ─────────────────────────────────────
if exist README.txt    copy /y README.txt    "%PUBLISH_DIR%\README.txt"    >nul
if exist CHANGELOG.txt copy /y CHANGELOG.txt "%PUBLISH_DIR%\CHANGELOG.txt" >nul
if exist LICENSE.txt   copy /y LICENSE.txt   "%PUBLISH_DIR%\LICENSE.txt"   >nul
echo  Files copied.
echo.

echo  Step 5/5 — Running Inno Setup Compiler...
echo  ───────────────────────────────────────────
if "%FAST_BUILD%"=="1" (
    echo  Skipping installer in FAST mode.
    goto DONE
)

if not exist %INNO_COMPILER% (
    echo  WARNING: Inno Setup not found at %INNO_COMPILER%
    echo  Install from https://jrsoftware.org/isinfo.php
    echo  Skipping installer creation.
    goto DONE
)

if not exist "installer_output" mkdir installer_output
%INNO_COMPILER% %ISS_FILE%
if %ERRORLEVEL% NEQ 0 ( echo  ERROR: Inno Setup failed & pause & exit /b 1 )
echo  Installer created in installer_output\

:DONE
echo.
echo  ============================================================
echo   BUILD COMPLETE — Summary
echo  ============================================================
echo.
echo   EXE     : %PUBLISH_DIR%\TDSPro.exe
if exist "installer_output\TDSPro_Setup_v%APP_VERSION%.exe" (
  echo   SETUP   : installer_output\TDSPro_Setup_v%APP_VERSION%.exe
  for %%A in ("installer_output\TDSPro_Setup_v%APP_VERSION%.exe") do echo   SIZE    : %%~zA bytes
)
echo.
if "%FAST_BUILD%"=="1" (
  echo   TIP: This was a FAST build. Run without FAST for the
  echo        compressed release EXE before distributing.
) else (
  echo   Deploy checklist:
  echo     [ ] Test installer on a clean Windows 10/11 machine
  echo     [ ] Test login (admin/admin) and change password
  echo     [ ] Add a deductor and run through Setup Wizard
  echo     [ ] Create one TDS entry and generate a 26Q FVU file
  echo     [ ] Verify crash.log location: %%APPDATA%%\TDSPro\crash.log
  echo     [ ] Verify backup created: %%APPDATA%%\TDSPro\Backup\
  echo     [ ] Sign the EXE with your code-signing cert (optional)
  echo     [ ] Upload to website and update version.json
)
echo.
pause
