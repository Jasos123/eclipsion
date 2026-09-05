#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
mkdir -p Scripts/logs
test_result=0
dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -c DebugOpt "$@" > Scripts/logs/Content.YAMLLinter.log 2>&1 || test_result=$?
cat Scripts/logs/Content.YAMLLinter.log
exit "$test_result"
