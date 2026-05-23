@echo off
setlocal
set VERSION=3.1.0

echo ============================================================
echo  TDS Pro v%VERSION% — Build and Package
echo ============================================================

:: ── Step 1: Kill running app ──────────────────────────────────────────────────
echo.
echo [1/5] Stopping TDSPro if running...
taskkill /F /IM TDSPro.exe 2>nul
timeout /t 1 /nobreak >nul

:: ── Step 2: Publish .NET app ──────────────────────────────────────────────────
echo.
echo [2/5] Publishing TDSPro.App (self-contained, win-x64, Release)...
dotnet publish TDSPro.App\TDSPro.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -o publish\win-x64 ^
    -p:PublishReadyToRun=true ^
    -p:PublishTrimmed=false ^
    -p:DeleteExistingFiles=true

if errorlevel 1 (
    echo.
    echo ERROR: dotnet publish failed.
    pause
    exit /b 1
)
echo    Publish output: publish\win-x64\

:: ── Step 3: Check bundled JRE ─────────────────────────────────────────────────
echo.
echo [3/5] Checking bundled JRE...
if exist "bundled_jre\bin\java.exe" (
    echo    Bundled JRE found: bundled_jre\bin\java.exe
) else (
    echo.
    echo    WARNING: bundled_jre\ folder not found!
    echo    The installer will work but Java must be installed manually by the user.
    echo.
    echo    To bundle JRE:
    echo    1. Download Eclipse Temurin JRE 8 (Windows x64 ZIP) from:
    echo       https://adoptium.net/temurin/releases/?version=8^&os=windows^&arch=x64^&package_type=jre
    echo    2. Extract the ZIP
    echo    3. Rename the extracted folder to "bundled_jre"
    echo    4. Place it next to this build_installer.bat file
    echo    5. Re-run this script
    echo.
    echo    Continuing without bundled JRE...
)

:: ── Step 4: Check FVU folder ──────────────────────────────────────────────────
echo.
echo [4/5] Checking FVU folder...
if exist "TDS_STANDALONE_FVU_9.4\TDS_STANDALONE_FVU_9.4.jar" (
    echo    FVU JARs found: TDS_STANDALONE_FVU_9.4\
) else (
    echo    WARNING: TDS_STANDALONE_FVU_9.4\TDS_STANDALONE_FVU_9.4.jar not found.
    echo    FVU validation will not be bundled. Users must configure it manually.
)

:: ── Step 5: Run Inno Setup ────────────────────────────────────────────────────
echo.
echo [5/5] Building installer with Inno Setup...

set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist %ISCC% (
    echo.
    echo    SKIPPED: Inno Setup not found at %ISCC%
    echo    Install Inno Setup 6 from: https://jrsoftware.org/isdl.php
    echo    Then re-run this script.
    echo.
    echo    Publish output is ready at: publish\win-x64\
    pause
    exit /b 0
)

mkdir installer_output 2>nul
%ISCC% TDSPro_Installer.iss

if errorlevel 1 (
    echo.
    echo ERROR: Inno Setup compilation failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  SUCCESS!
echo  Installer saved to: installer_output\
echo  Version: %VERSION%
echo ============================================================
echo.
echo Next steps:
echo  1. Test the installer on a clean machine
echo  2. Upload installer to: https://tdspro.in/download/
echo  3. Update version.json on the server
echo  4. Announce release to users
echo ============================================================
pause
