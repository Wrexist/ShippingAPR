# ShippingAPR Quick Installer
# Right-click this file and select "Run with PowerShell"
# Or run from terminal: powershell -ExecutionPolicy Bypass -File Install.ps1

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoUrl = "https://github.com/Wrexist/ShippingAPR"
$downloadUrl = "$repoUrl/releases/latest/download/ShippingAPR-Setup.exe"
$checksumUrl = "$downloadUrl.sha256"
$tempPath = Join-Path $env:TEMP "ShippingAPR-Setup.exe"
$checksumPath = "$tempPath.sha256"

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "   ShippingAPR - Quick Install" -ForegroundColor Cyan
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host ""

try {
    Write-Host "  Downloading latest ShippingAPR installer..." -ForegroundColor White

    $ProgressPreference = "Continue"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempPath -UseBasicParsing

    $size = [math]::Round((Get-Item $tempPath).Length / 1MB, 1)
    Write-Host "  Downloaded ($size MB)" -ForegroundColor Green

    # Verify the installer's integrity against the published SHA-256 before running it.
    Write-Host "  Verifying integrity..." -ForegroundColor White
    Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumPath -UseBasicParsing
    $expected = ((Get-Content $checksumPath -Raw).Trim() -split '\s+')[0].ToLower()
    $actual = (Get-FileHash -Path $tempPath -Algorithm SHA256).Hash.ToLower()
    if ($expected -ne $actual) {
        Remove-Item $tempPath, $checksumPath -ErrorAction SilentlyContinue
        throw "Checksum mismatch — the download may be corrupt or tampered with. Expected $expected but got $actual."
    }
    Write-Host "  Integrity verified." -ForegroundColor Green
    Remove-Item $checksumPath -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "  Starting installer..." -ForegroundColor White
    Write-Host "  (Follow the setup wizard to complete installation)" -ForegroundColor Gray
    Write-Host ""

    Start-Process -FilePath $tempPath -Wait

    Remove-Item $tempPath -ErrorAction SilentlyContinue
    Write-Host "  Done! Look for ShippingAPR on your desktop." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "  Download failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "  Try downloading manually from:" -ForegroundColor Yellow
    Write-Host "  $repoUrl/releases/latest" -ForegroundColor Yellow
}

Write-Host ""
Read-Host "  Press Enter to close"
