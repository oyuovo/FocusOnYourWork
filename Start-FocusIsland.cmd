@echo off
setlocal
if not exist "%~dp0artifacts\app\FocusIsland.exe" (
  echo Build the app first with scripts\build.ps1.
  pause
  exit /b 1
)
start "" "%~dp0artifacts\app\FocusIsland.exe"
