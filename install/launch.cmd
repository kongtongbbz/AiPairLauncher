@echo off
setlocal
set SCRIPT_DIR=%~dp0
set APP_DIR=%SCRIPT_DIR%app

if exist "%APP_DIR%\AiPairLauncher.App.exe" (
  start "" "%APP_DIR%\AiPairLauncher.App.exe"
  exit /b 0
)

powershell.exe -NoLogo -NoExit -Command "Write-Host 'AiPairLauncher.App.exe not found. Please build or publish the app first.'"
