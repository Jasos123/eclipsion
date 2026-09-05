#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
mkdir -p Scripts/logs
test_result=0
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt "$@" -- NUnit.ConsoleOut=0 NUnit.MapWarningTo=Failed > Scripts/logs/Content.IntegrationTests.log 2>&1 || test_result=$?
cat Scripts/logs/Content.IntegrationTests.log
exit "$test_result"
