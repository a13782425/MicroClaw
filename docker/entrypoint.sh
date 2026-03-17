#!/usr/bin/env sh
set -eu

MICROCLAW_HOME="${MICROCLAW_HOME:-/app/.microclaw}"

if [ -f "${MICROCLAW_HOME}/.env" ]; then
	set -a
	. "${MICROCLAW_HOME}/.env"
	set +a
fi

exec dotnet /app/gateway/microclaw.dll serve
