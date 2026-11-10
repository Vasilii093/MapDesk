param([string]$Destination=(Join-Path (Split-Path $PSScriptRoot -Parent) 'release-0.38'))
$ErrorActionPreference='Stop'
$Destination=[IO.Path]::GetFullPath($Destination)
if((Test-Path -LiteralPath $Destination) -or (Test-Path -LiteralPath ($Destination+'.zip'))){throw 'Папка или ZIP уже существуют. Укажите новое место выпуска.'}
$mapStage=Join-Path $env:TEMP ('MapDesk-release038-'+[Guid]::NewGuid().ToString('N'))
$mapBuild=Join-Path $mapStage 'build'
$mapChecks=Join-Path $mapStage 'Checks'
New-Item -ItemType Directory -Path $mapChecks -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -Destination $mapBuild
foreach($mapCheck in @('verify03.ps1','verify028.ps1','verify031.ps1','verify032.ps1','verify033.ps1','verify034.ps1','verify035.ps1','verify037.ps1','verify038.ps1','verify039.ps1','verify040.ps1')) {
    & (Join-Path $PSScriptRoot $mapCheck) -Distribution $mapBuild *> (Join-Path $mapChecks ($mapCheck+'.log'))
    Write-Output ($mapCheck+' — OK')
}
New-Item -ItemType Directory -Path $Destination | Out-Null
foreach($mapFile in @('MapDesk-x64.exe','MapDesk-x86.exe','MapDesk-x64.exe.config','MapDesk-x86.exe.config')){Copy-Item -LiteralPath (Join-Path $mapBuild $mapFile) -Destination $Destination}
foreach($mapAsset in @('default_map','native','proj-data','licenses')){Copy-Item -LiteralPath (Join-Path $mapBuild $mapAsset) -Destination $Destination -Recurse}
$mapSource=Join-Path $Destination 'Source'
New-Item -ItemType Directory -Path $mapSource | Out-Null
Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination $mapSource
foreach($mapFolder in @('Tests','assets')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $mapFolder) -Destination $mapSource -Recurse}
Copy-Item -LiteralPath $mapChecks -Destination $Destination -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-NOTES-0.38.md') -Destination (Join-Path $Destination 'README.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/MapDesk.ico') -Destination $Destination
Get-FileHash (Join-Path $Destination 'MapDesk-x64.exe'),(Join-Path $Destination 'MapDesk-x86.exe') | ForEach-Object {$_.Hash+'  '+[IO.Path]::GetFileName($_.Path)} | Set-Content -Encoding UTF8 (Join-Path $Destination 'SHA256.txt')
Compress-Archive -LiteralPath $Destination -DestinationPath ($Destination+'.zip')
Write-Output ('Выпуск готов: '+$Destination)
