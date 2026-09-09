@echo off
setlocal

rem Usage: publish.bat [platforms]
rem Platforms may be space separated (for example: "win-x64 win-x86").
set "platforms=%~1"
if /I "%~1"=="--no-pause" set "platforms="
if not defined platforms set "platforms=win-x64 win-x86 linux-x64 linux-arm64"

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" -RuntimeIdentifiers "%platforms%"
set "exit_code=%errorlevel%"
if /I not "%~1"=="--no-pause" if /I not "%~2"=="--no-pause" if /I not "%CODEX_NO_PAUSE%"=="1" pause
exit /b %exit_code%
