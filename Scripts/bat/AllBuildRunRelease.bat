@echo off
setlocal
call "%~dp0buildAllRelease.bat"
if errorlevel 1 exit /b %errorlevel%
call "%~dp0runQuickAll.bat" -c Release %*
exit /b %errorlevel%
