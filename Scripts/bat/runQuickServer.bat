@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
call dotnet run --project Content.Server --no-build %*
exit /b %errorlevel%
