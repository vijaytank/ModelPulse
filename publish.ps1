Write-Host "=================================================="
Write-Host "         MODELPULSE RELEASE PACKAGING             "
Write-Host "=================================================="

$outputDir = "publish-out"
$zipPath = "modelpulse-win-x64.zip"

if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "[INFO] Running dotnet publish for ModelPulse.UI..."
dotnet publish src/ModelPulse.UI/ModelPulse.UI.csproj -c Release -r win-x64 --self-contained false -o $outputDir

if ($LASTEXITCODE -eq 0) {
    Write-Host "[INFO] Compressing publish output to $zipPath..."
    Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath -Force
    Write-Host "[SUCCESS] ModelPulse successfully packaged to $zipPath!"
} else {
    Write-Host "[ERROR] Dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
