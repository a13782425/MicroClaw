@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d %~dp0

set "OUT=%~dp0dist"

REM ── 检查构建产物是否存在 ────────────────────────────────────────────────
if not exist "%OUT%\gateway\microclaw.dll" (
    echo ERROR: Build artifacts not found. Please run build.bat first.
    pause
    exit /b 1
)

REM ── 设置环境变量（对应 docker-compose.yml）─────────────────────────────
set "ASPNETCORE_ENVIRONMENT=Production"
set "ASPNETCORE_URLS=http://+:8080"
set "MICROCLAW_HOME=%OUT%\.microclaw"
set "MICROCLAW_WEBUI_PATH=%OUT%\webui"

REM ── 加载 .env 文件（如存在）────────────────────────────────────────────
if exist "%OUT%\.microclaw\.env" (
    echo Loading environment from .microclaw\.env ...
    for /f "usebackq tokens=* eol=#" %%i in ("%OUT%\.microclaw\.env") do (
        set "%%i"
    )
)

echo ============================================
echo  MicroClaw Starting...
echo  URL : http://localhost:8080
echo  Home: %OUT%\.microclaw
echo ============================================
echo.

dotnet "%OUT%\gateway\microclaw.dll" serve
