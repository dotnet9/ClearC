@echo off
setlocal
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -RuntimeIdentifier win-x64
set "exit_code=%errorlevel%"
if /I not "%~1"=="--no-pause" if /I not "%CODEX_NO_PAUSE%"=="1" pause
exit /b %exit_code%
