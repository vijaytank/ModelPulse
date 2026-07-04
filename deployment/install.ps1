# install.ps1 - Helper script to check prerequisites and run ModelPulse installer/build

Write-Host "🔬 ModelPulse Prerequisite Check..." -ForegroundColor Cyan

# Check if dotnet is installed
$dotnetInstalled = $false
try {
    $dotnetVersion = dotnet --version
    $dotnetInstalled = $true
    Write-Host "Found .NET CLI/SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "❌ .NET SDK or Runtime CLI not found on the system path." -ForegroundColor Red
}

# Check for .NET Desktop Runtime 10
$hasDesktopRuntime10 = $false
if ($dotnetInstalled) {
    $runtimes = dotnet --list-runtimes
    foreach ($runtime in $runtimes) {
        if ($runtime -match "Microsoft.WindowsDesktop.App 10\.") {
            $hasDesktopRuntime10 = $true
            Write-Host "Found .NET Desktop Runtime: $runtime" -ForegroundColor Green
        }
    }
}

if (-not $hasDesktopRuntime10) {
    Write-Host "⚠️ Warning: .NET Desktop Runtime 10.x (WPF) was not found in installed runtimes." -ForegroundColor Yellow
    Write-Host "You can download it from: https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Cyan
    
    $prompt = Read-Host "Would you like to proceed with the installation anyway? (y/N)"
    if ($prompt -ne "y" -and $prompt -ne "yes") {
        Write-Host "Installation aborted." -ForegroundColor Red
        Exit 1
    }
}

Write-Host "Prerequisites check passed. Proceeding with Winget package installer/script execution..." -ForegroundColor Green
# Trigger main installer script
& "$PSScriptRoot\..\Install-ModelPulse.ps1"
