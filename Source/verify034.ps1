param([string]$Distribution=(Join-Path $PSScriptRoot 'build'),[string]$DiagnosticWorkspace='')
$ErrorActionPreference='Stop'
$mapCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$mapSources=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
foreach($mapArch in @('x64','x86')) {
 $mapTest=Join-Path $Distribution ('OfflineTests-'+$mapArch+'.exe')
 & $mapCompiler /nologo /optimize+ /codepage:65001 /main:OfflineTests "/platform:$mapArch" "/out:$mapTest" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $mapSources (Join-Path $PSScriptRoot 'Tests/OfflineTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Workspace tools tests compile failed'}
 $mapArgs=@((Join-Path $PSScriptRoot ('verification/offline034-'+$mapArch+'-'+[Guid]::NewGuid().ToString('N'))))
 if($DiagnosticWorkspace -and $mapArch -eq 'x64'){$mapArgs+=$DiagnosticWorkspace}
 & $mapTest $mapArgs
 if($LASTEXITCODE -ne 0){throw "Workspace tools tests failed: $mapArch"}
}

