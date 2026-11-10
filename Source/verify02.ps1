param([string]$Distribution=(Join-Path $PSScriptRoot 'release02-final'),[switch]$Network)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'verify.ps1')
$mapCsc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$mapTests=Join-Path $PSScriptRoot 'testcore'
if(-not(Test-Path -LiteralPath $mapTests)){$mapTests=Join-Path $PSScriptRoot 'Tests'}
$mapRefs=@('/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Xml.Linq.dll','/r:System.IO.Compression.dll','/r:System.Drawing.dll','/r:System.Net.Http.dll')
foreach($mapTest in @('CatalogTests','SourceNetworkSmoke')){
 if($mapTest -eq 'SourceNetworkSmoke' -and -not $Network){continue}
 $mapExe=Join-Path $PSScriptRoot ('verification/'+$mapTest+'.exe')
 & $mapCsc /nologo /codepage:65001 "/out:$mapExe" $mapRefs (Join-Path $PSScriptRoot 'MapSources.cs') (Join-Path $PSScriptRoot 'Tiles.cs') (Join-Path $PSScriptRoot 'TileNetwork.cs') (Join-Path $PSScriptRoot 'BackgroundJobs.cs') (Join-Path $PSScriptRoot 'Diagnostics.cs') (Join-Path $mapTests ($mapTest+'.cs'))
 if($LASTEXITCODE -ne 0){throw 'Compile failed'}
 & $mapExe (Join-Path $PSScriptRoot 'verification/source-network')
 if($LASTEXITCODE -ne 0){throw "Test failed: $mapTest"}
}
foreach($mapArch in @('x64','x86')){
 $mapExe=Join-Path $Distribution ('ProjectionSmoke-'+$mapArch+'.exe')
 & $mapCsc /nologo /codepage:65001 "/platform:$mapArch" "/out:$mapExe" $mapRefs (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'Ldk.cs') (Join-Path $PSScriptRoot 'Projections.cs') (Join-Path $mapTests 'ProjectionSmoke.cs')
 if($LASTEXITCODE -ne 0){throw 'Projection test compile failed'}
 & $mapExe
 if($LASTEXITCODE -ne 0){throw 'Projection test failed'}
 $mapSmoke=Join-Path $PSScriptRoot ('verification/final-'+$mapArch)
 $mapProcess=Start-Process -FilePath (Join-Path $Distribution ('MapDesk-'+$mapArch+'.exe')) -ArgumentList @('--smoke-test',('"'+$mapSmoke+'"')) -WindowStyle Hidden -PassThru -Wait
 if($mapProcess.ExitCode -ne 0){throw 'Application smoke test failed'}
 Get-Content (Join-Path $mapSmoke 'result.txt')
}
