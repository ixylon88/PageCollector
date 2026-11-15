@echo off
setlocal
if exist "%~dp0dist\win-x64\PageCollector.exe" (
  start "" "%~dp0dist\win-x64\PageCollector.exe"
  exit /b 0
)
if exist "%~dp0dist\win-x64_self-contained\PageCollector.exe" (
  start "" "%~dp0dist\win-x64_self-contained\PageCollector.exe"
  exit /b 0
)
if not exist "%~dp0app\PageCollector.exe" (
  echo Please extract the complete ZIP before starting PageCollector.
  echo To build from source, use Build.cmd instead.
  pause
  exit /b 1
)
start "" "%~dp0app\PageCollector.exe"
