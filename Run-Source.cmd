@echo off
setlocal
pushd "%~dp0"
dotnet run --project src\PageCollector.App\PageCollector.App.csproj -c Release
set "run_result=%errorlevel%"
popd
if not "%run_result%"=="0" pause
exit /b %run_result%
