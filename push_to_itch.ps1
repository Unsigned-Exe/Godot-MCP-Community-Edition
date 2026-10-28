[CmdletBinding()]
param(
    [string]$TargetUser,
    [string]$GameSlug = "godot-mcp-community-edition"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Support passing full target like "username/slug"
if ($TargetUser -match '^([^/]+)/([^:]+)$') {
    $GameSlug = $matches[2]
    $TargetUser = $matches[1]
}

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "  Godot-MCP Community Edition -- itch.io Butler Dispatch" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Discover Butler binary
$ButlerExe = $null

$pathCmd = Get-Command "butler" -ErrorAction SilentlyContinue
if ($pathCmd) {
    $ButlerExe = $pathCmd.Source
} else {
    $brothDir = Join-Path $env:APPDATA "itch\broth\butler\versions"
    if (Test-Path $brothDir) {
        $found = Get-ChildItem -Path $brothDir -Filter "butler.exe" -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $ButlerExe = $found.FullName
        }
    }
}

if (-not $ButlerExe) {
    Write-Host "[ERROR] itch.io Butler CLI is not detected." -ForegroundColor Red
    Write-Host ""
    Write-Host "Options to get Butler:" -ForegroundColor Yellow
    Write-Host "  1. Windows Package Manager: winget install itch.butler"
    Write-Host "  2. Official installer:      https://itch.io/docs/butler/installing.html"
    Write-Host "  3. itch Desktop App:        https://itch.io/app (Butler is auto-bundled)"
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "[OK] Detected Butler binary at: $ButlerExe" -ForegroundColor Green
& "$ButlerExe" -V
Write-Host ""

# 2. Verify / Prompt for target user
if (-not $TargetUser) {
    $TargetUser = Read-Host "Enter your itch.io username or organization"
}

if ([string]::IsNullOrWhiteSpace($TargetUser)) {
    Write-Host "[ERROR] No username specified. Aborting." -ForegroundColor Red
    exit 1
}

# 3. Verify archive exists
$ZipPath = Join-Path $ScriptDir "dist\GodotMCP-v1.0.3-windows-64.zip"
if (-not (Test-Path $ZipPath)) {
    Write-Host "[ERROR] Distribution archive not found at: $ZipPath" -ForegroundColor Red
    Write-Host "Please build the project first."
    exit 1
}

$channel = "$TargetUser/${GameSlug}:windows-64"
Write-Host "Pushing $ZipPath to $channel (version 1.0.3)..." -ForegroundColor Cyan

& "$ButlerExe" push "$ZipPath" "$channel" --userversion "1.0.3"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "[SUCCESS] Successfully deployed Godot-MCP to itch.io ($channel)!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[FAILED] Butler push exited with code $LASTEXITCODE." -ForegroundColor Red
}
