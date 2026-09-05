#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
sh ./runQuickServer.sh "$@" &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
sh ./runQuickClient.sh "$@"
