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
  echo [ERROR] The .NET Framework 4 compiler built into Windows was not found.
  echo         It is normally present on Windows 10/11. Try running Windows Update once.
  echo.
  pause
  exit /b 1
)

echo Closing MouseSwitch first if it is running...
taskkill /IM MouseSwitch.exe /F >nul 2>&1

echo Compiling...
"%CSC%" /nologo /target:winexe /platform:%PLAT% /optimize+ /codepage:65001 ^
  /out:MouseSwitch.exe /win32manifest:app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Security.dll ^
  src\*.cs

if errorlevel 1 (
  echo.
  echo [FAILED] Please copy the error messages above as-is and report them.
  echo.
  pause
  exit /b 1
)

echo.
echo [DONE] MouseSwitch.exe has been created.

set "SAC=0"
for /f "tokens=3" %%v in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy" /v VerifiedAndReputablePolicyState 2^>nul ^| find "0x"') do set "SAC=%%v"
if /i "%SAC%"=="0x1" echo [WARNING] Smart App Control is turned on, so this exe will be blocked from running. It will not start automatically either.
echo.
pause
