$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = '宝塔网络代理修复'
$src = Join-Path $root ('bin\' + $app + '.exe')
if (-not (Test-Path $src)) { Write-Host ('找不到 ' + $src + '，请先运行 build.cmd 编译。'); exit 1 }

$dest = Join-Path $env:LOCALAPPDATA $app
Get-Process -Name $app -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
if (-not (Test-Path $dest)) { New-Item -ItemType Directory $dest | Out-Null }

Copy-Item $src (Join-Path $dest ($app + '.exe')) -Force
$cfg = Join-Path $root 'config.ini'
if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $dest 'config.ini') -Force }

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name $app -Value ('"' + (Join-Path $dest ($app + '.exe')) + '"')

Start-Process (Join-Path $dest ($app + '.exe'))
Write-Host ('安装完成，已启动并设置开机自启: ' + $dest)