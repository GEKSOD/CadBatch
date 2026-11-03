@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-user.ps1"
if errorlevel 1 (
  echo Installation failed. Copy the error above for the developer.
) else (
  echo Installation completed. Start AutoCAD and enter CADBATCHINFO.
)
pause
