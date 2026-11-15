@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0Verify.ps1" %*
exit /b %errorlevel%
