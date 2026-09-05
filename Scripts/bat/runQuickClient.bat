@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
call dotnet run --project Content.Client --no-build %*
exit /b %errorlevel%
