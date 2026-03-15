#!/usr/bin/env sh
set -eu

if [ -f /app/.env ]; then
	set -a
	. /app/.env
	set +a
fi

exec dotnet /app/gateway/MicroClaw.Gateway.WebApi.dll
