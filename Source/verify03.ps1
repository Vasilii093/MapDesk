param([string]$Distribution=(Join-Path $PSScriptRoot 'release03'),[switch]$Network)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'verify02.ps1') -Distribution $Distribution
$viewTests=Join-Path $PSScriptRoot 'testcore'
if(-not(Test-Path -LiteralPath $viewTests)){$viewTests=Join-Path $PSScriptRoot 'Tests'}
$viewFiles=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$viewFiles+=Join-Path $viewTests 'ViewTests.cs'
$viewFiles+=Join-Path $viewTests 'EditorTests.cs'
$viewFiles+=Join-Path $viewTests 'ProfileTests.cs'
$viewFiles+=Join-Path $viewTests 'LiveRayTests.cs'
$viewFiles+=Join-Path $viewTests 'LayerTests.cs'
foreach($viewArch in @('x64','x86')){
 $viewExe=Join-Path $Distribution ('ViewTests-'+$viewArch+'.exe')
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /codepage:65001 /main:ViewTests "/platform:$viewArch" "/out:$viewExe" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $viewFiles
 if($LASTEXITCODE -ne 0){throw 'View tests compile failed'}
 $viewArgs=@((Join-Path $PSScriptRoot ('verification/view03-'+$viewArch)))
 if($Network -and $viewArch -eq 'x64'){$viewArgs+='--network'}
 & $viewExe $viewArgs
 if($LASTEXITCODE -ne 0){throw 'View test failed'}
}



