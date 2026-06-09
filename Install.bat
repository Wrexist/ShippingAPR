@echo off
title ShippingAPR Installer
echo.
echo  ========================================
echo   ShippingAPR - Quick Install
echo  ========================================
echo.
echo  Downloading the latest installer...
echo.

powershell -ExecutionPolicy Bypass -Command ^
  "$url = 'https://github.com/Wrexist/ShippingAPR/releases/latest/download/ShippingAPR-Setup.exe'; " ^
  "$out = Join-Path $env:TEMP 'ShippingAPR-Setup.exe'; " ^
  "$sum = $out + '.sha256'; " ^
  "try { " ^
  "  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " ^
  "  Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing; " ^
  "  Invoke-WebRequest -Uri ($url + '.sha256') -OutFile $sum -UseBasicParsing; " ^
  "  $expected = ((Get-Content $sum -Raw).Trim() -split '\s+')[0].ToLower(); " ^
  "  $actual = (Get-FileHash -Path $out -Algorithm SHA256).Hash.ToLower(); " ^
  "  if ($expected -ne $actual) { throw 'Checksum mismatch - download may be corrupt or tampered with.' }; " ^
  "  Write-Host '  Integrity verified. Starting installer...' -ForegroundColor Green; " ^
  "  Start-Process -FilePath $out -Wait; " ^
  "  Remove-Item $out, $sum -ErrorAction SilentlyContinue; " ^
  "} catch { " ^
  "  Write-Host ('  Install failed: ' + $_.Exception.Message) -ForegroundColor Red; " ^
  "  Write-Host '  You can also download manually from:' -ForegroundColor Yellow; " ^
  "  Write-Host '  https://github.com/Wrexist/ShippingAPR/releases/latest' -ForegroundColor Yellow; " ^
  "  exit 1; " ^
  "}"

if %errorlevel% neq 0 (
    echo.
    echo  Install failed. Try downloading manually from:
    echo  https://github.com/Wrexist/ShippingAPR/releases/latest
    echo.
)

pause
