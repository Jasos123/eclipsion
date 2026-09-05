#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
mkdir -p Scripts/logs
test_result=0
dotnet test Content.Tests/Content.Tests.csproj -c DebugOpt "$@" -- NUnit.ConsoleOut=0 > Scripts/logs/Content.Tests.log 2>&1 || test_result=$?
cat Scripts/logs/Content.Tests.log
exit "$test_result"
