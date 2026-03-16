@echo off
cd /d %~dp0src\webui
echo Installing dependencies...
call npm install
if %ERRORLEVEL% neq 0 (
    echo npm install failed
    pause
    exit /b 1
)
echo Building frontend...
call npm run build
if %ERRORLEVEL% neq 0 (
    echo Build failed
    pause
    exit /b 1
)
echo Done. Start the backend from IDE and visit http://localhost:5080
pause
