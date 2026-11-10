param([switch]$Network)
$ErrorActionPreference = 'Stop'
$mapVerifyCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$mapTestSources = Join-Path $PSScriptRoot 'Tests'
if (-not (Test-Path -LiteralPath $mapTestSources)) { $mapTestSources = Join-Path $PSScriptRoot 'testcore' }
$mapVerifyOutput = Join-Path $PSScriptRoot 'verification'
New-Item -ItemType Directory -Path $mapVerifyOutput -Force | Out-Null
$mapCommonRefs = @('/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Xml.Linq.dll','/r:System.IO.Compression.dll','/r:System.Net.Http.dll','/r:System.Device.dll')
foreach ($mapTestName in @('CoreTests','GpsTests','TilesSmoke')) {
 $mapTestTarget = Join-Path $mapVerifyOutput ($mapTestName + '.exe')
 & $mapVerifyCompiler /nologo /target:exe /codepage:65001 "/out:$mapTestTarget" $mapCommonRefs (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'Ldk.cs') (Join-Path $PSScriptRoot 'Gps.cs') (Join-Path $PSScriptRoot 'Tiles.cs') (Join-Path $PSScriptRoot 'TileNetwork.cs') (Join-Path $PSScriptRoot 'BackgroundJobs.cs') (Join-Path $PSScriptRoot 'Diagnostics.cs') (Join-Path $PSScriptRoot 'MapSources.cs') (Join-Path $mapTestSources ($mapTestName + '.cs'))
 if ($LASTEXITCODE -ne 0) { throw "Compile failed: $mapTestName" }
 & $mapTestTarget
 if ($LASTEXITCODE -ne 0) { throw "Test failed: $mapTestName" }
}
if ($Network) {
 $mapNetTarget = Join-Path $mapVerifyOutput 'NetworkSmoke.exe'
 & $mapVerifyCompiler /nologo /target:exe /codepage:65001 "/out:$mapNetTarget" $mapCommonRefs (Join-Path $PSScriptRoot 'Tiles.cs') (Join-Path $PSScriptRoot 'TileNetwork.cs') (Join-Path $PSScriptRoot 'BackgroundJobs.cs') (Join-Path $PSScriptRoot 'Diagnostics.cs') (Join-Path $PSScriptRoot 'MapSources.cs') (Join-Path $mapTestSources 'NetworkSmoke.cs')
 if ($LASTEXITCODE -ne 0) { throw 'Network test compile failed' }
 & $mapNetTarget (Join-Path $mapVerifyOutput 'network-cache')
 if ($LASTEXITCODE -ne 0) { throw 'Network test failed' }
}
