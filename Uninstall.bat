@echo off
setlocal
title .BAK - Uninstall
taskkill /f /im BAK.exe >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v BAK /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ExtensionWheel /f >nul 2>&1
timeout /t 1 /nobreak >nul
rmdir /s /q "%LOCALAPPDATA%\Programs\BAK" >nul 2>&1
echo .BAK was removed.
echo Your settings and history are in %%APPDATA%%\BAK - delete that folder too if you want them gone.
pause
