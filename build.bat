@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "PLAT=x64"
if not exist "%CSC%" (
  set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
  set "PLAT=anycpu"
)
if not exist "%CSC%" (
  echo.
  echo [오류] 윈도우에 내장된 .NET Framework 4 컴파일러를 찾을 수 없습니다.
  echo        Windows 10/11 이라면 보통 그냥 있습니다. 윈도우 업데이트를 한 번 해보세요.
  echo.
  pause
  exit /b 1
)

echo 실행 중이면 먼저 종료합니다...
taskkill /IM MouseSwitch.exe /F >nul 2>&1

echo 컴파일 중...
"%CSC%" /nologo /target:winexe /platform:%PLAT% /optimize+ /codepage:65001 ^
  /out:MouseSwitch.exe /win32manifest:app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Security.dll ^
  src\*.cs

if errorlevel 1 (
  echo.
  echo [실패] 위의 오류 메시지를 그대로 복사해서 알려주세요.
  echo.
  pause
  exit /b 1
)

echo.
echo [완료] MouseSwitch.exe 가 만들어졌습니다.

set "SAC=0"
for /f "tokens=3" %%v in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState 2^>nul ^| find "0x"') do set "SAC=%%v"
if /i "%SAC%"=="0x1" echo [주의] 스마트 앱 컨트롤이 켜져 있어서 이 exe 는 실행이 차단됩니다. 자동 실행도 안 됩니다.
echo.
pause
