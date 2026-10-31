@echo off
rem Builds ISCSIConsolePlus without installing anything system-wide.
rem Usage: build.cmd [build.ps1 arguments]
setlocal

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo Windows PowerShell was not found.
    exit /b 1
)

powershell.exe -NoProfile -File "%~dp0build.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo Build failed with exit code %EXITCODE%.
    pause
)
exit /b %EXITCODE%
