@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0Build.ps1" -Publish %*
exit /b %errorlevel%
