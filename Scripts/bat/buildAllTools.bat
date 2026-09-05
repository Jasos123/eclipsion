@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
call git submodule update --init --recursive
if errorlevel 1 exit /b %errorlevel%
call dotnet build -c Tools %*
exit /b %errorlevel%
