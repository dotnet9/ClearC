@echo off
setlocal
call "%~dp0publish.bat" "linux-x64" %*
exit /b %errorlevel%
