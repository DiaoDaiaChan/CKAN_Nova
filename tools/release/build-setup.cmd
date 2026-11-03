@echo off
rem One-click build: double-click to produce CkanUISetup.exe and portable zip.
rem The real logic lives in Build-Setup.ps1 next to this file; args are forwarded, e.g.:
rem   build-setup.cmd -Clean -RunSetup
rem   build-setup.cmd -ZipOnly
rem (Kept in tools/release/ because verify-layout H-01 whitelists the repo root only.)
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Setup.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
    echo [FAIL] exit code %RC% - see log above.
) else (
    echo [DONE] artifacts in artifacts\release
)
pause
exit /b %RC%
