@echo off
setlocal
title CDeTDS — Build Installer

echo ============================================================
echo  CDeTDS Installer Build
echo ============================================================
echo.

:: ── Step 1: Kill any running instance ────────────────────────
echo [1/5] Stopping CDeTDS if running...
taskkill /IM CDeTDS.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

:: ── Step 2: Clean publish_out completely ──────────────────────
echo [2/5] Cleaning publish_out...
if exist "publish_out" (
    :: Use robocopy MIR trick to delete deeply nested folders (bypasses MAX_PATH limit)
    mkdir "publish_out\_empty_tmp" >nul 2>&1
    robocopy "publish_out\_empty_tmp" "publish_out\publish" /MIR /NFL /NDL /NJH /NJS >nul 2>&1
    rmdir /s /q "publish_out\publish" >nul 2>&1
    rmdir /s /q "publish_out" >nul 2>&1
)

:: ── Step 3: Publish (self-contained, Release, win-x64) ───────
echo [3/5] Publishing application...
dotnet publish CDeTDS.App\CDeTDS.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishReadyToRun=true ^
    -o "publish_out"

if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    pause & exit /b 1
)

:: Remove any nested publish subfolder created by pubxml
if exist "publish_out\publish" (
    mkdir "publish_out\_empty_tmp" >nul 2>&1
    robocopy "publish_out\_empty_tmp" "publish_out\publish" /MIR /NFL /NDL /NJH /NJS >nul 2>&1
    rmdir /s /q "publish_out\publish" >nul 2>&1
    rmdir /s /q "publish_out\_empty_tmp" >nul 2>&1
)
echo    Published OK.

:: ── Step 4: Check prerequisites ──────────────────────────────
echo [4/5] Checking prerequisites...
if not exist "publish_out\CDeTDS.exe" (
    echo ERROR: CDeTDS.exe not found in publish_out.
    pause & exit /b 1
)
if not exist "MicrosoftEdgeWebview2Setup.exe"    echo WARNING: MicrosoftEdgeWebview2Setup.exe missing — WebView2 won't auto-install.
if not exist "TDS_STANDALONE_FVU_9.4"            echo WARNING: TDS_STANDALONE_FVU_9.4 folder missing.
if not exist "installer\bundled_jre\bin\java.exe" echo WARNING: bundled_jre missing — FVU needs Java on target machine.
if not exist "LICENSE.txt"                        echo WARNING: LICENSE.txt missing.

:: ── Step 5: Run Inno Setup ───────────────────────────────────
echo [5/5] Building installer...
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe"       set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"

if not defined ISCC (
    echo ERROR: Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php
    pause & exit /b 1
)

"%ISCC%" "CDeTDS_Installer.iss"
if errorlevel 1 (
    echo ERROR: Inno Setup build failed.
    pause & exit /b 1
)

echo.
echo ============================================================
echo  BUILD COMPLETE
echo  Installer saved to: installer_output\
echo ============================================================
echo.
start "" "installer_output"
pause
