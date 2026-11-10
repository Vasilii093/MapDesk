param([string]$Distribution=(Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference='Stop'
$workspaceCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$workspaceSources=Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName
foreach($workspaceArch in @('x64','x86')) {
 $workspaceTest=Join-Path $Distribution ('WorkspaceTests-'+$workspaceArch+'.exe')
 & $workspaceCompiler /nologo /codepage:65001 /main:WorkspaceTests "/platform:$workspaceArch" "/out:$workspaceTest" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $workspaceSources (Join-Path $PSScriptRoot 'Tests/WorkspaceTests.cs')
 if($LASTEXITCODE -ne 0){throw 'Workspace tests compile failed'}
 & $workspaceTest (Join-Path $PSScriptRoot ('verification/workspace028-'+$workspaceArch))
 if($LASTEXITCODE -ne 0){throw "Workspace tests failed: $workspaceArch"}
}
