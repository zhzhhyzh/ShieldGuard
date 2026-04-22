# ScreenGuard AI - Installer Script
# Run via install.bat (double-click) or: powershell -ExecutionPolicy Bypass -File install.ps1

$ErrorActionPreference = "Stop"
$AppName = "ScreenGuard AI"
$ExeName = "ScreenGuardAI.exe"
$ConfigName = "appsettings.json"
$InstallDir = Join-Path $env:LOCALAPPDATA "ScreenGuardAI"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   ScreenGuard AI - Installer" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

# --- Verify source files exist ---
$srcExe = Join-Path $ScriptDir $ExeName
$srcConfig = Join-Path $ScriptDir $ConfigName

if (-not (Test-Path $srcExe)) {
    Write-Host "[ERROR] $ExeName not found in $ScriptDir" -ForegroundColor Red
    Write-Host "Make sure the installer files are in the same folder." -ForegroundColor Yellow
    exit 1
}

# --- Kill running instance ---
$running = Get-Process -Name "ScreenGuardAI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "[*] Stopping running ScreenGuard AI..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

# --- Create install directory ---
Write-Host "[*] Installing to: $InstallDir" -ForegroundColor Cyan
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# --- Copy files ---
Write-Host "[*] Copying application files..." -ForegroundColor Cyan
Copy-Item $srcExe (Join-Path $InstallDir $ExeName) -Force

# Only copy config if it doesn't already exist (preserve user settings)
$destConfig = Join-Path $InstallDir $ConfigName
if (Test-Path $srcConfig) {
    if (-not (Test-Path $destConfig)) {
        Copy-Item $srcConfig $destConfig -Force
        Write-Host "    - Default config created" -ForegroundColor DarkGray
    } else {
        Write-Host "    - Existing config preserved" -ForegroundColor DarkGray
    }
}

# --- Create Start Menu shortcut ---
Write-Host "[*] Creating Start Menu shortcut..." -ForegroundColor Cyan
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "$AppName.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDir $ExeName
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "AI-powered interview assistant with screen capture protection"
$shortcut.IconLocation = (Join-Path $InstallDir $ExeName) + ",0"
$shortcut.Save()

# --- Create Desktop shortcut ---
Write-Host "[*] Creating Desktop shortcut..." -ForegroundColor Cyan
$desktopPath = [Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktopPath "$AppName.lnk"
$shortcut2 = $shell.CreateShortcut($desktopShortcut)
$shortcut2.TargetPath = Join-Path $InstallDir $ExeName
$shortcut2.WorkingDirectory = $InstallDir
$shortcut2.Description = "AI-powered interview assistant with screen capture protection"
$shortcut2.IconLocation = (Join-Path $InstallDir $ExeName) + ",0"
$shortcut2.Save()

# --- Register auto-start on login ---
Write-Host "[*] Registering auto-start on login..." -ForegroundColor Cyan
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$regValue = "`"$(Join-Path $InstallDir $ExeName)`""
Set-ItemProperty -Path $regPath -Name "ScreenGuardAI" -Value $regValue

# --- Copy uninstaller ---
$uninstallBat = Join-Path $ScriptDir "uninstall.bat"
$uninstallPs1 = Join-Path $ScriptDir "uninstall.ps1"
if (Test-Path $uninstallBat) {
    Copy-Item $uninstallBat (Join-Path $InstallDir "uninstall.bat") -Force
}
if (Test-Path $uninstallPs1) {
    Copy-Item $uninstallPs1 (Join-Path $InstallDir "uninstall.ps1") -Force
}

# --- Done ---
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "   Installation Complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Install location : $InstallDir" -ForegroundColor White
Write-Host "  Start Menu       : $shortcutPath" -ForegroundColor White
Write-Host "  Desktop shortcut : $desktopShortcut" -ForegroundColor White
Write-Host "  Auto-start       : Enabled" -ForegroundColor White
Write-Host ""
Write-Host "  To uninstall, run uninstall.bat from:" -ForegroundColor DarkGray
Write-Host "  $InstallDir" -ForegroundColor DarkGray
Write-Host ""

# --- Ask to launch ---
$launch = Read-Host "Launch ScreenGuard AI now? (Y/n)"
if ($launch -ne "n" -and $launch -ne "N") {
    Start-Process (Join-Path $InstallDir $ExeName)
    Write-Host "  Launched! Look for the green shield icon in the system tray." -ForegroundColor Green
}
