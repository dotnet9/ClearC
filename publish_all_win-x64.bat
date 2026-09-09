@echo off
setlocal
call "%~dp0publish_win-x64.bat" %*
exit /b %errorlevel%
