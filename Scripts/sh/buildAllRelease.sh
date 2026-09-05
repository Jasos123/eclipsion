#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/../.."
git submodule update --init --recursive
dotnet build -c Release "$@"
