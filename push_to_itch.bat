@echo off
setlocal
cd /d "%~dp0"

echo =======================================================
echo   Godot-MCP Community Edition -- itch.io Butler Dispatch
echo =======================================================
echo.

where butler >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] itch.io Butler CLI is not detected in your PATH.
    echo.
    echo To install Butler via WinGet:
    echo   winget install itch.butler
    echo.
    echo Or download directly from:
    echo   https://itch.io/docs/butler/installing.html
    echo.
    pause
    exit /b 1
)

set TARGET_USER=%~1
if "%TARGET_USER%"=="" (
    set /p TARGET_USER="Enter your itch.io username or organization: "
)

if "%TARGET_USER%"=="" (
    echo [ERROR] No username specified. Aborting.
    pause
    exit /b 1
)

set ZIP_PATH=dist\GodotMCP-v1.0.0-windows-64.zip
if not exist "%ZIP_PATH%" (
    echo [ERROR] %ZIP_PATH% not found. Run a build first!
    pause
    exit /b 1
)

echo.
echo Pushing %ZIP_PATH% to %TARGET_USER%/godot-mcp:windows-64 ...
butler push "%ZIP_PATH%" %TARGET_USER%/godot-mcp:windows-64 --userversion 1.0.0

if %ERRORLEVEL% equ 0 (
    echo.
    echo [SUCCESS] Successfully deployed Godot-MCP to itch.io!
) else (
    echo.
    echo [FAILED] Butler push encountered an error (code: %ERRORLEVEL%).
)

pause
