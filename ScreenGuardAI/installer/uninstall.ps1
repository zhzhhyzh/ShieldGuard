# ScreenGuard AI - Uninstaller Script
# Run via uninstall.bat or: powershell -ExecutionPolicy Bypass -File uninstall.ps1

$ErrorActionPreference = "SilentlyContinue"
$AppName = "ScreenGuard AI"
$InstallDir = Join-Path $env:LOCALAPPDATA "ScreenGuardAI"

Write-Host ""
Write-Host "============================================" -ForegroundColor Red
Write-Host "   ScreenGuard AI - Uninstaller" -ForegroundColor Red
Write-Host "============================================" -ForegroundColor Red
Write-Host ""

$confirm = Read-Host "Are you sure you want to uninstall ScreenGuard AI? (y/N)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Uninstall cancelled." -ForegroundColor Yellow
    exit 0
}

# --- Kill running instance ---
$running = Get-Process -Name "ScreenGuardAI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running ScreenGuard AI..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# --- Remove auto-start registry entry ---
Write-Host "[*] Removing auto-start entry..." -ForegroundColor Cyan
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $regPath -Name "ScreenGuardAI" -ErrorAction SilentlyContinue

# --- Remove Start Menu shortcut ---
Write-Host "[*] Removing Start Menu shortcut..." -ForegroundColor Cyan
$startMenuShortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
if (Test-Path $startMenuShortcut) {
    Remove-Item $startMenuShortcut -Force
}

# --- Remove Desktop shortcut ---
Write-Host "[*] Removing Desktop shortcut..." -ForegroundColor Cyan
$desktopPath = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktopPath "$AppName.lnk"
if (Test-Path $desktopShortcut) {
    Remove-Item $desktopShortcut -Force
}

# --- Remove install directory ---
Write-Host "[*] Removing application files..." -ForegroundColor Cyan
if (Test-Path $InstallDir) {
    # Remove all files except the uninstaller currently running
    Get-ChildItem $InstallDir -File | Where-Object { $_.Name -ne "uninstall.bat" -and $_.Name -ne "uninstall.ps1" } | Remove-Item -Force
    
    # Also remove user data if desired
    $removeData = Read-Host "Also remove user settings from %APPDATA%\ScreenGuardAI? (y/N)"
    if ($removeData -eq "y" -or $removeData -eq "Y") {
        $appDataDir = Join-Path $env:APPDATA "ScreenGuardAI"
        if (Test-Path $appDataDir) {
            Remove-Item $appDataDir -Recurse -Force
            Write-Host "    - User settings removed" -ForegroundColor DarkGray
        }
    }
    
    # Schedule self-delete of remaining files
    Start-Process cmd.exe -ArgumentList "/c", "timeout /t 2 /nobreak >nul & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden
}

# --- Done ---
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   Uninstall Complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  ScreenGuard AI has been removed from your system." -ForegroundColor White
Write-Host ""
