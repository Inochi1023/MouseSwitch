@echo off
cd /d "%~dp0"
net session >nul 2>&1
if errorlevel 1 (
  echo 관리자 권한으로 실행하세요.
  pause
  exit /b 1
)
schtasks /delete /tn "MouseSwitch" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseSwitch /f >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch UDP" >nul 2>&1
echo 자동 실행과 방화벽 규칙을 제거했습니다.
pause
