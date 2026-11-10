param([string]$Source=$PSScriptRoot,[string]$Destination=(Join-Path $PSScriptRoot 'perf-after'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$perfFiles=Get-ChildItem -LiteralPath $Source -Filter '*.cs' | ForEach-Object {$_.FullName}
$perfTest=Join-Path $PSScriptRoot 'testcore/PerfBench.cs'
if(-not(Test-Path -LiteralPath $perfTest)){$perfTest=Join-Path $PSScriptRoot 'Tests/PerfBench.cs'}
$perfFiles+=$perfTest
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /optimize+ /codepage:65001 /main:PerfBench /platform:x64 "/out:$Destination/PerfBench.exe" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $perfFiles
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
$perfAssets=Join-Path $PSScriptRoot 'default_map'
if(-not(Test-Path -LiteralPath $perfAssets)){$perfAssets=Join-Path (Split-Path $PSScriptRoot -Parent) 'default_map'}
Copy-Item -LiteralPath $perfAssets -Destination $Destination -Recurse -Force
& (Join-Path $Destination 'PerfBench.exe')
