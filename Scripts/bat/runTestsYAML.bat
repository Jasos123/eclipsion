@echo off
setlocal
cd /d "%~dp0..\.." || exit /b 1
if not exist Scripts\logs mkdir Scripts\logs
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c DebugOpt %* > Scripts\logs\Content.YAMLLinter.log 2>&1
set "test_result=%errorlevel%"
type Scripts\logs\Content.YAMLLinter.log
exit /b %test_result%
