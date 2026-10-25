@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo =======================================================
echo   Godot-MCP Community Edition -- itch.io Butler Dispatch
echo =======================================================
echo.

set BUTLER_EXE=
where butler >nul 2>nul
if %ERRORLEVEL% equ 0 (
    set BUTLER_EXE=butler
) else (
    for /f "delims=" %%I in ('dir /b /s "%APPDATA%\itch\broth\butler\versions\butler.exe" 2^>nul') do (
        set BUTLER_EXE=%%I
    )
)

if "!BUTLER_EXE!"=="" (
    echo [ERROR] itch.io Butler CLI is not detected on your system.
    echo.
    echo Options to install Butler:
    echo   1. Windows Package Manager: winget install itch.butler
    echo   2. Official itch installer: https://itch.io/docs/butler/installing.html
    echo   3. Install the itch.io Desktop App (which bundles Butler automatically)
    echo.
    pause
    exit /b 1
)

echo [OK] Using Butler binary at: "!BUTLER_EXE!"
"!BUTLER_EXE!" -V
echo.

set TARGET_USER=%~1
if "%TARGET_USER%"=="" (
    set /p TARGET_USER="Enter your itch.io username or organization: "
)

if "%TARGET_USER%"=="" (
    echo [ERROR] No username specified. Aborting.
    pause
    exit /b 1
)

set ZIP_PATH=dist\GodotMCP-v1.0.1-windows-64.zip
if not exist "%ZIP_PATH%" (
    echo [ERROR] %ZIP_PATH% not found. Run a build first!
    pause
    exit /b 1
)

echo.
echo Pushing %ZIP_PATH% to %TARGET_USER%/godot-mcp-community-edition:windows-64 ...
"!BUTLER_EXE!" push "%ZIP_PATH%" %TARGET_USER%/godot-mcp-community-edition:windows-64 --userversion 1.0.1

if %ERRORLEVEL% equ 0 (
    echo.
    echo [SUCCESS] Successfully deployed Godot-MCP to itch.io!
) else (
    echo.
    echo [FAILED] Butler push encountered an error (code: %ERRORLEVEL%).
)

pause
