@echo off
rem Sons Of The Forest - Mod Picker launcher
rem Runs ModPicker.ps1 with the built-in Windows PowerShell 5.1.
rem "-ExecutionPolicy Bypass" applies to this one process only; system settings are not changed.
setlocal
title Sons Of The Forest - Mod Picker
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0ModPicker.ps1" %*
if errorlevel 1 (
    echo.
    echo [Mod Picker] Error - see "%~dp0logs\fatal.log"
    pause
)
endlocal
