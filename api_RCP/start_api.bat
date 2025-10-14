@echo off
setlocal

REM ================== ChronoSyncRCP API Startup ==================
REM Configure host/port and admin token for the FastAPI server.
REM Adjust these values on the remote host accordingly.

REM Listen on all interfaces so LAN clients (Unity) can connect via 192.168.x.x
if "%HOST%"=="" set "HOST=0.0.0.0"

REM Default HTTP port for REST and (optionally) WebSocket endpoint if served by same app
if "%PORT%"=="" set "PORT=8100"

REM Admin token required by /admin endpoints (header: X-Admin-Token)
REM Replace this with a strong secret on the remote host or set it as a system/user env var and remove this line.
if "%ADMIN_TOKEN%"=="" set "ADMIN_TOKEN=CHRONOSYNC-ADMIN-LOCAL"

REM Ensure 'src\server' on PYTHONPATH for imports (common, ws)
cd /d "%~dp0\src\server\api"
set "PYTHONPATH=%CD%\..;%PYTHONPATH%"

REM Start Uvicorn (fallback to python -m if uvicorn not on PATH), binding to HOST:PORT
where uvicorn >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    python -m uvicorn main:app --host %HOST% --port %PORT%
) else (
    uvicorn main:app --host %HOST% --port %PORT%
)

pause
endlocal
