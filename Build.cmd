@echo off
setlocal
pushd "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install the .NET 10 SDK to build from source.
  echo https://dotnet.microsoft.com/download/dotnet/10.0
  popd
  pause
  exit /b 1
)
dotnet publish src\PageCollector.App\PageCollector.App.csproj -c Release -r win-x64 --self-contained true -o dist\win-x64
set "build_result=%errorlevel%"
if "%build_result%"=="0" echo Ready: dist\win-x64\PageCollector.exe
popd
pause
exit /b %build_result%
