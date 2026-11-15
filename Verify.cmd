@echo off
setlocal
pushd "%~dp0"
dotnet run --project tests\PageCollector.Checks -c Release
set "test_result=%errorlevel%"
popd
pause
exit /b %test_result%
