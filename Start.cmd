@echo off
setlocal
if not exist "%~dp0app\PageCollector.exe" (
  echo Please extract the complete ZIP before starting PageCollector.
  echo To build from source, use Build.cmd instead.
  pause
  exit /b 1
)
start "" "%~dp0app\PageCollector.exe"
