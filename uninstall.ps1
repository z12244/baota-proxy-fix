$ErrorActionPreference = 'SilentlyContinue'
$app = '宝塔网络代理修复'
$dest = Join-Path $env:LOCALAPPDATA $app
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Remove-ItemProperty -Path $runKey -Name $app -ErrorAction SilentlyContinue
Get-Process -Name $app -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Set-Location $env:TEMP
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ('已卸载 ' + $app + '。')