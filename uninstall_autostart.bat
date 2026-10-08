@echo off
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo Please run this file as administrator.
  pause
  exit /b 1
)
schtasks /delete /tn "MouseSwitch" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseSwitch /f >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch UDP" >nul 2>&1
echo Removed the automatic start entry and the firewall rules.
pause
