@echo off
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo Administrator rights are required. Right-click this file and choose "Run as administrator".
  pause
  exit /b 1
)

if not exist "%~dp0MouseSwitch.exe" (
  echo MouseSwitch.exe was not found. Run build.bat first.
  pause
  exit /b 1
)

rem ---- Check Smart App Control (1 = on) ----
set "SAC=0"
for /f "tokens=3" %%v in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState 2^>nul ^| find "0x"') do set "SAC=%%v"
if /i "%SAC%"=="0x1" (
  echo.
  echo  ************************************************************
  echo   Smart App Control is turned on.
  echo   While it is on, Windows blocks MouseSwitch at logon, so it never
  echo   starts automatically. The task is still registered, but
  echo   you must turn Smart App Control off for automatic start to work.
  echo  ************************************************************
  echo.
)

echo [1/3] Adding firewall rules...
netsh advfirewall firewall delete rule name="MouseSwitch TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch UDP" >nul 2>&1
netsh advfirewall firewall add rule name="MouseSwitch TCP" dir=in action=allow protocol=TCP localport=24800 profile=private,domain >nul
netsh advfirewall firewall add rule name="MouseSwitch UDP" dir=in action=allow protocol=UDP localport=24801 profile=private,domain >nul

echo [2/3] Registering automatic start at logon...
rem Register the task directly instead of going through the exe. An older exe did not know the register command, so a failure looked like success.
rem Administrator rights / runs on battery / no time limit / normal priority
set "MS_EXE=%~dp0MouseSwitch.exe"
set "MS_DIR=%~dp0"
schtasks /delete /tn "MouseSwitch" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseSwitch /f >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u=$env:USERDOMAIN+'\'+$env:USERNAME; $a=New-ScheduledTaskAction -Execute $env:MS_EXE -WorkingDirectory $env:MS_DIR; $t=New-ScheduledTaskTrigger -AtLogOn -User $u; $p=New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan) -Priority 4 -MultipleInstances IgnoreNew; Register-ScheduledTask -TaskName 'MouseSwitch' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null"

echo [3/3] Verifying...
schtasks /query /tn "MouseSwitch" >nul 2>&1
if errorlevel 1 goto failed

echo   Registration verified: MouseSwitch will start automatically with administrator rights from the next logon.
if /i "%SAC%"=="0x1" echo   Note: it stays blocked until Smart App Control is turned off.
echo.
echo If you move this folder, run this file again.
pause
exit /b 0

:failed
echo   Registration failed. Please capture the contents of this window and report it.
pause
exit /b 1
