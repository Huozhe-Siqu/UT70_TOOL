@echo off
setlocal
call "%~dp0build.bat" -IfNeeded
if errorlevel 1 (
  echo.
  echo Build failed. Review the error above.
  pause
  exit /b 1
)
start "UT70 Power Lab" /D "%~dp0bin\Release" "%~dp0bin\Release\UT70Dashboard.exe"
