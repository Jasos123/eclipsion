@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
if not exist Scripts\logs mkdir Scripts\logs
dotnet test Content.Tests/Content.Tests.csproj -c DebugOpt %* -- NUnit.ConsoleOut=0 > Scripts\logs\Content.Tests.log 2>&1
set "test_result=%errorlevel%"
type Scripts\logs\Content.Tests.log
exit /b %test_result%
