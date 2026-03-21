@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d %~dp0

set "OUT=%~dp0dist"

echo ============================================
echo  MicroClaw Local Build
echo  Output: %OUT%
echo ============================================
echo.

REM ── Step 1: 清理构建产物（不动 .microclaw 配置目录）──────────────────
echo [1/4] Cleaning build artifacts...
if exist "%OUT%\gateway" (
    rmdir /s /q "%OUT%\gateway"
    echo   Removed dist\gateway
)
if exist "%OUT%\webui" (
    rmdir /s /q "%OUT%\webui"
    echo   Removed dist\webui
)
echo.

REM ── Step 2: 构建 Gateway（.NET）────────────────────────────────────────
echo [2/4] Building gateway (dotnet publish)...
dotnet publish src\gateway\MicroClaw\MicroClaw.csproj ^
    -c Release ^
    -o "%OUT%\gateway" ^
    /p:UseAppHost=false ^
    /p:DebugType=none ^
    /p:DebugSymbols=false
if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: Gateway build failed.
    pause
    exit /b 1
)
echo.

REM ── Step 3: 构建 WebUI（Node / Vite）──────────────────────────────────
echo [3/4] Building webui (npm)...
pushd src\webui
call npm ci
if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: npm ci failed.
    popd
    pause
    exit /b 1
)
call npm run build
if %ERRORLEVEL% neq 0 (
    echo.
    echo ERROR: webui build failed.
    popd
    pause
    exit /b 1
)
popd
xcopy /e /i /y "src\webui\dist" "%OUT%\webui\" >nul
echo.

REM ── Step 4: 确保 .microclaw 目录存在（不删除，保留配置）──────────────
echo [4/4] Ensuring .microclaw directory exists...
if not exist "%OUT%\.microclaw" (
    mkdir "%OUT%\.microclaw"
    echo   Created dist\.microclaw
) else (
    echo   dist\.microclaw already exists, skipped.
)
echo.

echo ============================================
echo  Build complete!
echo.
echo  Directory structure:
echo    dist\
echo    ├── gateway\    (dotnet publish output)
echo    ├── webui\      (vite build output)
echo    └── .microclaw\ (config, NOT cleared on rebuild)
echo.
echo  Run with:
echo    dotnet "%OUT%\gateway\Tools.dll" serve
echo ============================================
pause
