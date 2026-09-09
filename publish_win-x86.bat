@echo off
setlocal
call "%~dp0publish.bat" "win-x86" %*
exit /b %errorlevel%
