@echo off
setlocal
title .BAK - Install

echo Building .BAK ...
call "%~dp0build.bat" nopause
if errorlevel 1 (
  pause
  exit /b 1
)

set "DEST=%LOCALAPPDATA%\Programs\BAK"

echo Stopping any running copy ...
taskkill /f /im BAK.exe >nul 2>&1
taskkill /f /im ExtensionWheel.exe >nul 2>&1
timeout /t 1 /nobreak >nul

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0dist\BAK.exe" "%DEST%\BAK.exe" >nul
if errorlevel 1 (
  echo Could not copy BAK.exe to "%DEST%".
  pause
  exit /b 1
)

rem remove the previous name of this tool, if it was installed
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ExtensionWheel /f >nul 2>&1
if exist "%LOCALAPPDATA%\Programs\ExtensionWheel" rmdir /s /q "%LOCALAPPDATA%\Programs\ExtensionWheel" >nul 2>&1

echo Starting ...
start "" "%DEST%\BAK.exe"
echo.
echo Done. Installed to: %DEST%
echo It starts with Windows automatically (you can turn that off in Settings).
timeout /t 4 >nul
