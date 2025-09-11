@echo off
setlocal EnableExtensions

REM --- Asennuskansion juuri ---
set "DIR=%~dp0"
if "%DIR:~-1%"=="\" (set "ROOT=%DIR:~0,-1%") else (set "ROOT=%DIR%")

REM --- Server EXE (onefile tai alikansio) ---
set "SERVER_EXE=%ROOT%\SOWServer.exe"
if not exist "%SERVER_EXE%" set "SERVER_EXE=%ROOT%\SOWServer\SOWServer.exe"

if not exist "%SERVER_EXE%" (
  echo [ERROR] SOWServer.exe not found in "%ROOT%" or "%ROOT%\SOWServer"
  exit /b 1
)

REM --- Käynnistä serveri minimissä oikealla juuressa ---
start "" /min "%SERVER_EXE%" --bind 127.0.0.1 --port 8324 --root "%ROOT%"

REM --- Käynnistä GUI piilossa taustaprosessin kautta, joka odottaa ja tappaa serverin ---
REM Huom: asetamme SOWB_ROOTin GUI:lle PowerShellissä, jotta GUI kirjoittaa samaan juureen.
start "" powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command ^
 "$env:SOWB_ROOT='%ROOT%'; $p = Start-Process -FilePath '%ROOT%\SOWBroadcast.exe' -PassThru; ^
  $p.WaitForExit(); Start-Process -WindowStyle Hidden cmd -ArgumentList '/c taskkill /IM SOWServer.exe /F >nul 2>&1'"

REM --- Batch päättyy nyt heti; ikkuna sulkeutuu ---
exit /b
