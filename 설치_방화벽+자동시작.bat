@echo off
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
  echo 관리자 권한이 필요합니다. 이 파일을 오른쪽 클릭 - "관리자 권한으로 실행" 하세요.
  pause
  exit /b 1
)

if not exist "%~dp0MouseSwitch.exe" (
  echo MouseSwitch.exe 가 없습니다. 먼저 build.bat 을 실행하세요.
  pause
  exit /b 1
)

rem ---- 스마트 앱 컨트롤 확인 (1 = 켜짐) ----
set "SAC=0"
for /f "tokens=3" %%v in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState 2^>nul ^| find "0x"') do set "SAC=%%v"
if /i "%SAC%"=="0x1" (
  echo.
  echo  ************************************************************
  echo   스마트 앱 컨트롤이 켜져 있습니다.
  echo   켜져 있는 동안은 윈도우가 로그인할 때 MouseSwitch 를 차단해서
  echo   자동 실행이 절대 되지 않습니다. 등록은 해두지만,
  echo   자동으로 켜지게 하려면 스마트 앱 컨트롤을 꺼야 합니다.
  echo  ************************************************************
  echo.
)

echo [1/3] 방화벽 규칙 추가...
netsh advfirewall firewall delete rule name="MouseSwitch TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="MouseSwitch UDP" >nul 2>&1
netsh advfirewall firewall add rule name="MouseSwitch TCP" dir=in action=allow protocol=TCP localport=24800 profile=private,domain >nul
netsh advfirewall firewall add rule name="MouseSwitch UDP" dir=in action=allow protocol=UDP localport=24801 profile=private,domain >nul

echo [2/3] 로그인 시 자동 실행 등록...
rem exe 를 거치지 않고 직접 등록한다. (예전 exe 는 등록 명령을 몰라서 실패해도 성공으로 보였음)
rem 관리자 권한 / 배터리에서도 실행 / 시간 제한 없음 / 보통 우선순위
set "MS_EXE=%~dp0MouseSwitch.exe"
set "MS_DIR=%~dp0"
schtasks /delete /tn "MouseSwitch" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v MouseSwitch /f >nul 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -Command "$u=$env:USERDOMAIN+'\'+$env:USERNAME; $a=New-ScheduledTaskAction -Execute $env:MS_EXE -WorkingDirectory $env:MS_DIR; $t=New-ScheduledTaskTrigger -AtLogOn -User $u; $p=New-ScheduledTaskPrincipal -UserId $u -LogonType Interactive -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan) -Priority 4 -MultipleInstances IgnoreNew; Register-ScheduledTask -TaskName 'MouseSwitch' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null"

echo [3/3] 확인...
schtasks /query /tn "MouseSwitch" >nul 2>&1
if errorlevel 1 goto failed

echo   등록 확인됨: 다음 로그인부터 관리자 권한으로 자동 실행됩니다.
if /i "%SAC%"=="0x1" echo   (단, 스마트 앱 컨트롤을 끄기 전까지는 차단됩니다)
echo.
echo 폴더를 옮기면 이 파일을 다시 실행하세요.
pause
exit /b 0

:failed
echo   등록에 실패했습니다. 이 창의 내용을 캡처해서 보내주세요.
pause
exit /b 1
