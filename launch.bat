@echo off
setlocal

rem Asennuskansio (päättyy aina \). Poista lopun \, jotta --root toimii.
set "DIR=%~dp0"
if "%DIR:~-1%"=="\" (set "ROOT=%DIR:~0,-1%") else (set "ROOT=%DIR%")

rem Sama juuri GUI:lle (jos SOWBroadcast.py lukee SOWB_ROOTin)
set "SOWB_ROOT=%ROOT%"

pushd "%ROOT%"

rem Etsi serveri (onefile tai one-folder)
set "SERVER_EXE=%ROOT%\SOWServer.exe"
if not exist "%SERVER_EXE%" set "SERVER_EXE=%ROOT%\SOWServer\SOWServer.exe"

if not exist "%SERVER_EXE%" (
  echo [ERROR] SOWServer.exe not found in "%ROOT%" or "%ROOT%\SOWServer"
  pause
  popd & endlocal & exit /b 1
)

rem >>> Käynnistä serveri SUORAAN exe:nä (ei pythonilla)
tasklist /FI "IMAGENAME eq SOWServer.exe" | find /I "SOWServer.exe" >nul
if errorlevel 1 (
  start "" /min "%SERVER_EXE%" --bind 127.0.0.1 --port 8324 --root "%ROOT%"
  timeout /t 1 >nul
)

rem Käynnistä GUI ja odota sen sulkeutumista
"%ROOT%\SOWBroadcast.exe"

rem Sulje serveri
taskkill /IM SOWServer.exe /F >nul 2>&1

popd
endlocal
