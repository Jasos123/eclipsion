#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
exec dotnet run --project Content.Server --no-build "$@"
