@echo off
setlocal
call "%~dp0publish.bat" "linux-arm64" %*
exit /b %errorlevel%
