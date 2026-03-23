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
  "try { " ^
  "  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " ^
  "  Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing; " ^
  "  Write-Host '  Download complete. Starting installer...' -ForegroundColor Green; " ^
  "  Start-Process -FilePath $out -Wait; " ^
  "  Remove-Item $out -ErrorAction SilentlyContinue; " ^
  "} catch { " ^
  "  Write-Host '  Download failed. Please check your internet connection.' -ForegroundColor Red; " ^
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
