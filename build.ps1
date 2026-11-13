$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { Write-Host '找不到 .NET Framework 自带的 csc.exe。'; exit 1 }

$binDir = Join-Path $root 'bin'
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory $binDir | Out-Null }

$out = Join-Path $binDir '宝塔网络代理修复.exe'
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out" /r:System.dll (Join-Path $root 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { Write-Host '编译失败。'; exit 1 }
Write-Host ('编译完成: ' + $out + '  (' + (Get-Item $out).Length + ' 字节)')