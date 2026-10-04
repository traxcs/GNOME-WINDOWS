@echo off
rem ------------------------------------------------------------------
rem  GnomeWin - emergency restore of the Windows taskbar.
rem  Works even if GnomeWin.exe is missing or broken.
rem ------------------------------------------------------------------
echo Restoring the Windows taskbar...

rem 1. Ask GnomeWin to quit and restore everything itself (if available).
set "GW=%LOCALAPPDATA%\Programs\GnomeWin\GnomeWin.exe"
if exist "%~dp0GnomeWin.exe" set "GW=%~dp0GnomeWin.exe"
if exist "%GW%" (
    "%GW%" --restore
    timeout /t 2 /nobreak >nul
)

rem 2. Make sure no GnomeWin process is left.
taskkill /im GnomeWin.exe /f >nul 2>&1

rem 3. Restarting explorer always recreates a visible taskbar.
taskkill /im explorer.exe /f >nul 2>&1
start explorer.exe

echo.
echo Done. If the taskbar is set to auto-hide and you did not want it:
echo   Settings ^> Personalization ^> Taskbar ^> Taskbar behaviors ^> uncheck "Automatically hide the taskbar".
pause
