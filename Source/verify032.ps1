param([string]$Distribution=(Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference='Stop'
$mapCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$mapSources=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
foreach($mapArch in @('x64','x86')) {
 $mapTest=Join-Path $Distribution ('OptimizationTests-'+$mapArch+'.exe')
 & $mapCompiler /nologo /codepage:65001 /main:OptimizationTests "/platform:$mapArch" "/out:$mapTest" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $mapSources (Join-Path $PSScriptRoot 'Tests/OptimizationTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Optimization tests compile failed'}
 & $mapTest (Join-Path $PSScriptRoot ('verification/optimization032-'+$mapArch+'-'+[Guid]::NewGuid().ToString('N')))
 if($LASTEXITCODE -ne 0){throw "Optimization tests failed: $mapArch"}
}
