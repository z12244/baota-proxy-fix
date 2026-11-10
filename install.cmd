@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "APPNAME=宝塔网络代理修复"
set "SRC=%~dp0bin\%APPNAME%.exe"
set "DEST=%LOCALAPPDATA%\%APPNAME%"

if not exist "%SRC%" (
    echo 找不到 %SRC%，请先运行 build.cmd 编译。
    pause
    exit /b 1
)

taskkill /f /im "%APPNAME%.exe" >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%" "%DEST%\" >nul
if exist "%~dp0config.ini" copy /y "%~dp0config.ini" "%DEST%\" >nul

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "%APPNAME%" /t REG_SZ /d "\"%DEST%\%APPNAME%.exe\"" /f >nul

start "" "%DEST%\%APPNAME%.exe"
echo 安装完成，已启动并设置开机自启。
echo 目录: %DEST%
pause
