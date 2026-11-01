@echo off
rem One-click build + launch for CKAN UI. Double-click this file, or run it from a shell.
setlocal
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-run.ps1" %*
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" (
  echo.
  echo [failed] build-and-run.ps1 exited with %EXITCODE%
)
if "%~1"=="" pause
exit /b %EXITCODE%
