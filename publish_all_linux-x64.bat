@echo off
setlocal
call "%~dp0publish_linux-x64.bat" %*
exit /b %errorlevel%
