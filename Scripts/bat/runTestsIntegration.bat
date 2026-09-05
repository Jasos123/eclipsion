@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
if not exist Scripts\logs mkdir Scripts\logs
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt %* -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed > Scripts\logs\Content.IntegrationTests.log 2>&1
set "test_result=%errorlevel%"
type Scripts\logs\Content.IntegrationTests.log
exit /b %test_result%
