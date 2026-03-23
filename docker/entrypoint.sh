#!/usr/bin/env sh
set -eu

MICROCLAW_HOME="${MICROCLAW_HOME:-/app/.microclaw}"

if [ -f "${MICROCLAW_HOME}/.env" ]; then
    # 清理 Windows 行尾符
    sed -i 's/\r//' "${MICROCLAW_HOME}/.env"
    set -a
    . "${MICROCLAW_HOME}/.env"
    set +a
fi

exec dotnet /app/gateway/microclaw.dll serve
