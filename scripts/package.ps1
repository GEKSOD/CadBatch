param([switch]$NoPublish,[string]$RuntimeVersion='10.0.14')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$version=([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version
$dotnet=Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){$dotnet='dotnet'}
$env:DOTNET_CLI_HOME=Join-Path $projectRoot '.tools\cli-home'
$env:MSBuildEnableWorkloadResolver='false'
$app=Join-Path $projectRoot ('artifacts\desktop-'+$version)
if(-not $NoPublish){
 & $dotnet publish 'src\CadBatch.Desktop\CadBatch.Desktop.csproj' -c Release -f net10.0-windows -r win-x64 --self-contained true ("-p:RuntimeFrameworkVersion="+$RuntimeVersion) -p:DebugType=None -p:DebugSymbols=false --packages .packages -m:1 -nr:false -o $app -v minimal
 if($LASTEXITCODE -ne 0){throw 'Autonomous application publish failed'}
}
if(-not(Test-Path -LiteralPath (Join-Path $app 'CadBatch.exe'))){throw 'Publish the standalone application first'}
$release=Join-Path $projectRoot ('artifacts\release\CadBatch-'+$version)
$bundle=Join-Path $release 'CadBatch.bundle'
$desktop=Join-Path $release 'App'
$docs=Join-Path $release 'docs'
New-Item -ItemType Directory -Path $bundle,$desktop,$docs -Force | Out-Null
foreach($pair in @(@('net48','2023-2024'),@('net8.0-windows','2025-2026'),@('net10.0-windows','2027'))){
 $source=Join-Path $projectRoot ('src\CadBatch.AutoCAD\bin\Release\'+$pair[0])
 $target=Join-Path $bundle ('Contents\'+$pair[1])
 New-Item -ItemType Directory -Path $target -Force | Out-Null
 Get-ChildItem -LiteralPath $source -Filter '*.dll' -File | ForEach-Object {
  if($_.Name -match '^(AcCoreMgd|AcDbMgd|AcMgd|AdWindows|AcWindows|AdUi|AcCui)'){throw 'Autodesk runtime is not distributable with this package'}
  Copy-Item -LiteralPath $_.FullName -Destination $target -Force
 }
 $deps=Join-Path $source 'CadBatch.AutoCAD.deps.json'
 if(Test-Path -LiteralPath $deps){Copy-Item -LiteralPath $deps -Destination $target -Force}
 if(Test-Path -LiteralPath (Join-Path $source 'runtimes')){Copy-Item -LiteralPath (Join-Path $source 'runtimes') -Destination $target -Recurse -Force}
}
# PDF worker: a separate process for PDF work when AutoCAD has an incompatible PDF dependency loaded.
$workerTarget=Join-Path $bundle 'Contents\PdfWorker'
New-Item -ItemType Directory -Path $workerTarget -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\CadBatch.PdfWorker\bin\Release\net48') -File | Where-Object { $_.Extension -in '.dll','.exe','.config' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $workerTarget -Force }
if(-not(Test-Path -LiteralPath (Join-Path $workerTarget 'CadBatch.PdfWorker.exe'))){throw 'PDF worker is missing'}
# Single source of the version: Directory.Build.props. The manifest template carries 0.0.0.
$manifest=(Get-Content -LiteralPath 'packaging\PackageContents.xml' -Raw -Encoding UTF8) -replace 'AppVersion="[0-9.]+"',('AppVersion="'+$version+'"') -replace '(<ComponentEntry AppName="CadBatch" )Version="[0-9.]+"',('$1Version="'+$version+'"')
[System.IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'),$manifest,(New-Object System.Text.UTF8Encoding($true)))
Get-ChildItem -LiteralPath $app -Force | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $desktop -Recurse -Force}
# The guide has one source (docs/Start.md); HTML and TXT are built here.
$guide=Join-Path $projectRoot ('artifacts\guide-'+$version)
python 'scripts\make-docs.py' $version $guide
if($LASTEXITCODE -ne 0){throw 'Guide build failed'}
foreach($name in @('Start.html','Start.txt','Start.md')){Copy-Item -LiteralPath (Join-Path $guide $name) -Destination $docs -Force}
foreach($name in @('Architecture.md','Testing.md',('Release-'+$version+'.md'))){Copy-Item -LiteralPath (Join-Path 'docs' $name) -Destination $docs -Force}
Copy-Item -LiteralPath 'packaging\THIRD-PARTY-NOTICES.txt' -Destination $docs -Force
$licenses=Join-Path $docs 'runtime-licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
foreach($item in @(@('microsoft.netcore.app.runtime.win-x64','LICENSE.TXT','NETCore-LICENSE.txt'),@('microsoft.netcore.app.runtime.win-x64','THIRD-PARTY-NOTICES.TXT','NETCore-NOTICES.txt'),@('microsoft.windowsdesktop.app.runtime.win-x64','LICENSE','WindowsDesktop-LICENSE.txt'))){
 $license=Join-Path $projectRoot ('.packages\'+$item[0]+'\'+$RuntimeVersion+'\'+$item[1])
 Copy-Item -LiteralPath $license -Destination (Join-Path $licenses $item[2]) -Force
}
Copy-Item -LiteralPath 'samples' -Destination $docs -Recurse -Force
Copy-Item -LiteralPath (Join-Path $guide 'Start.html') -Destination (Join-Path $release 'Start.html') -Force
Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $release 'LICENSE.txt') -Force
Copy-Item -LiteralPath 'packaging\THIRD-PARTY-NOTICES.txt' -Destination $release -Force
@{id='CadBatch.Desktop';version=$version;runtime=$RuntimeVersion;architecture='win-x64'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'product.json') -Encoding utf8
$payload=Join-Path $projectRoot ('artifacts\packaging-'+$version)
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$env:CADBATCH_PACKAGE_ROOT=$release
$env:CADBATCH_PAYLOAD_DIR=$payload
$env:CADBATCH_PACKAGE_VERSION=$version
python 'scripts\make-payload.py'
if($LASTEXITCODE -ne 0){throw 'Payload creation failed'}
$payloadFile=Join-Path $payload 'Payload.zip'
& $dotnet build 'src\CadBatch.Setup\CadBatch.Setup.csproj' -c Release --no-restore -m:1 -nr:false ("-p:PayloadFile="+$payloadFile) -p:DebugType=None -p:DebugSymbols=false -v minimal
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$downloads=Join-Path $projectRoot ('artifacts\downloads-'+$version)
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
Copy-Item -LiteralPath 'src\CadBatch.Setup\bin\Release\net48\CadBatch.Setup.exe' -Destination (Join-Path $downloads ('CadBatch-Setup-'+$version+'.exe')) -Force
$env:CADBATCH_DOWNLOAD_DIR=$downloads
python 'scripts\make-portable.py'
if($LASTEXITCODE -ne 0){throw 'Portable package creation failed'}
Write-Output $downloads
