@echo off
setlocal
cd /d "%~dp0"

set "NODE_EXE="
where node >nul 2>nul && set "NODE_EXE=node"
if not defined NODE_EXE if exist "C:\Program Files\nodejs\node.exe" set "NODE_EXE=C:\Program Files\nodejs\node.exe"
if not defined NODE_EXE if exist "C:\Program Files (x86)\nodejs\node.exe" set "NODE_EXE=C:\Program Files (x86)\nodejs\node.exe"

if not defined NODE_EXE (
    echo Node.js was not found.
    echo Install the LTS version from https://nodejs.org and run this again.
    pause
    exit /b 1
)

start "Stream Deck Customizer" /min "%NODE_EXE%" "%~dp0customizer\server.js"
timeout /t 2 /nobreak >nul
start "" "http://127.0.0.1:8787"
