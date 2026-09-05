@echo off
setlocal
start "Eclipsion server" /d "%~dp0" cmd /c call "%~dp0runQuickServer.bat" %*
start "Eclipsion client" /d "%~dp0" cmd /c call "%~dp0runQuickClient.bat" %*
exit /b %errorlevel%
