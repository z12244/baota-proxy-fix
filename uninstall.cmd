@echo off
setlocal
chcp 65001 >nul
cd /d "%TEMP%"

set "APPNAME=宝塔网络代理修复"
set "DEST=%LOCALAPPDATA%\%APPNAME%"

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "%APPNAME%" /f >nul 2>&1
taskkill /f /im "%APPNAME%.exe" >nul 2>&1
timeout /t 1 /nobreak >nul
rd /s /q "%DEST%" >nul 2>&1

echo 已卸载 %APPNAME%（代理例外规则会保留，不影响正常上网）。
pause
