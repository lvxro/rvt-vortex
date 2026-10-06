@echo off
title RVT Vortex Installer
echo.
echo ========================================
echo    RVT Vortex Installer
echo ========================================
echo.
echo Starting installation...
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0install.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Installation failed.
    echo Read the red message above. If a file is in use, quit your AI client completely
    echo (Claude Desktop: tray icon ^> Quit) and run this again. Otherwise try right-click ^> Run as administrator.
    echo.
)

pause
