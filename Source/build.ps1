param([string]$Destination = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$mapCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $mapCompiler)) { $mapCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $mapCompiler)) { throw 'Нужен компилятор .NET Framework 4.x.' }
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$mapSourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
foreach ($mapArchitecture in @('x64','x86')) {
    $mapExe = Join-Path $Destination ('MapDesk-' + $mapArchitecture + '.exe')
    & $mapCompiler /nologo /warnaserror+ /target:winexe "/win32icon:$(Join-Path $PSScriptRoot 'assets/MapDesk.ico')" /optimize+ /codepage:65001 "/platform:$mapArchitecture" "/out:$mapExe" "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.IO.Compression.dll /r:System.Net.Http.dll /r:System.Device.dll $mapSourceFiles
    if ($LASTEXITCODE -ne 0) { throw "Сборка $mapArchitecture завершилась с ошибкой." }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MapDesk.exe.config') -Destination ($mapExe + '.config') -Force
}
foreach ($mapAsset in @('default_map','native','proj-data','licenses')) {
 $mapAssetPath=Join-Path $PSScriptRoot $mapAsset
 if (-not (Test-Path -LiteralPath $mapAssetPath)) { $mapAssetPath=Join-Path (Split-Path $PSScriptRoot -Parent) $mapAsset }
 if ((Test-Path -LiteralPath $mapAssetPath) -and ([IO.Path]::GetFullPath($mapAssetPath) -ne [IO.Path]::GetFullPath((Join-Path $Destination $mapAsset)))) { Copy-Item -LiteralPath $mapAssetPath -Destination $Destination -Recurse -Force }
}
Write-Output "Собраны x64 и x86: $Destination"




