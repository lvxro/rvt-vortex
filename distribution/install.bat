@echo off
title RVT Vortex

rem Everything happens in install.ps1 (and what it draws lives in lib\VortexUi.ps1).
rem It asks Windows for administrator permission and carries on in a new window,
rem so this one only stays open when that could not even start.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo   The installer did not finish. Read the message above.
    echo   If a file is in use, quit your AI client completely
    echo   (Claude Desktop: tray icon ^> Quit^) and run this again.
    echo.
    pause
)
