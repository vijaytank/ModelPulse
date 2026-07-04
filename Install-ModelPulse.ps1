# Install-ModelPulse.ps1 - User-facing installer for ModelPulse

$ErrorActionPreference = "Stop"

# 1. Check for Admin Rights
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "❌ Error: This script must be run with Administrator privileges." -ForegroundColor Red
    Write-Host "Please open a PowerShell console as Administrator and run the script again." -ForegroundColor Yellow
    Exit 1
}

Write-Host "🚀 Starting ModelPulse Installation..." -ForegroundColor Cyan

$InstallDir = "C:\Program Files\ModelPulse"
$ReleaseUrl = "https://github.com/vijaytank/ModelPulse/releases/latest/download/ModelPulse.zip"
$TempZip = "$env:TEMP\ModelPulse.zip"

# Create installation directory
if (-not (Test-Path $InstallDir)) {
    New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null
    Write-Host "Created installation directory: $InstallDir" -ForegroundColor Green
}

# 2. Build from source if local repo is detected, otherwise download ZIP
$localSource = Join-Path $PSScriptRoot "src\ModelPulse.UI\ModelPulse.UI.csproj"
if (Test-Path $localSource) {
    Write-Host "📦 Local repository detected. Compiling and publishing from source..." -ForegroundColor Cyan
    try {
        dotnet publish $localSource -c Release -r win-x64 --self-contained false -o $InstallDir | Out-Null
        Write-Host "Published local build to $InstallDir successfully!" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Warning: Failed to publish local source code. Falling back to release binary download..." -ForegroundColor Yellow
        $localSource = $null
    }
}

if ($null -eq $localSource) {
    Write-Host "📥 Downloading latest binaries from: $ReleaseUrl" -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri $ReleaseUrl -OutFile $TempZip -UseBasicParsing
        Write-Host "Extraction files to: $InstallDir" -ForegroundColor Cyan
        Expand-Archive -Path $TempZip -DestinationPath $InstallDir -Force
        Remove-Item -Path $TempZip -Force
        Write-Host "Downloaded and installed release binaries." -ForegroundColor Green
    } catch {
        Write-Host "❌ Error: Failed to download release binaries from GitHub." -ForegroundColor Red
        Write-Host "Details: $_" -ForegroundColor Yellow
        Exit 1
    }
}

# 3. Initialize default settings in %APPDATA%\ModelPulse\settings.json if missing
$AppDataDir = Join-Path $env:APPDATA "ModelPulse"
if (-not (Test-Path $AppDataDir)) {
    New-Item -Path $AppDataDir -ItemType Directory -Force | Out-Null
}
$SettingsFile = Join-Path $AppDataDir "settings.json"
if (-not (Test-Path $SettingsFile)) {
    Write-Host "⚙️ Creating default settings.json in $AppDataDir" -ForegroundColor Cyan
    $DefaultSettings = @{
        polling_mode = "default"
        polling_intervals = @{
            idle_ms = 3000
            active_ms = 1000
        }
        runtimes = @{
            ollama = @{
                enabled = $true
                endpoint = "http://127.0.0.1:11434"
            }
            llama_cpp = @{
                enabled = $false
                endpoint = "http://127.0.0.1:8080"
            }
        }
        alerts = @{
            suppressed_types = @()
            cooldown_seconds = 60
            vram_warning_threshold_percent = 90.0
        }
        ui = @{
            always_on_top = $true
            opacity = 0.9
            launch_overlay_on_startup = $false
        }
    }
    $DefaultSettings | ConvertTo-Json -Depth 5 | Out-File $SettingsFile -Encoding utf8
}

# 4. Add ModelPulse to Environment Path
$currentPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($currentPath -notlike "*$InstallDir*") {
    Write-Host "➕ Adding ModelPulse installation directory to system environment path..." -ForegroundColor Cyan
    $newPath = $currentPath + ";" + $InstallDir
    [Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
    # Refresh PATH in current session
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
    Write-Host "Environment Path updated." -ForegroundColor Green
}

# 5. Provide Run Instructions
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "🎉 ModelPulse installed successfully!" -ForegroundColor Green
Write-Host "Installation location: $InstallDir" -ForegroundColor White
Write-Host "To start the application, run:" -ForegroundColor White
Write-Host "  Start-Process '$InstallDir\ModelPulse.UI.exe'" -ForegroundColor Yellow
Write-Host "Or if you open a new terminal window, simply type:" -ForegroundColor White
Write-Host "  ModelPulse.UI.exe" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Cyan
