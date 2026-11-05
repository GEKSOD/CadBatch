param([switch]$Restore)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $projectRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'
New-Item -ItemType Directory -Force -Path 'artifacts\verification' | Out-Null
if ($Restore) {
    & $sdkCommand restore CadBatch.sln --packages .packages --disable-parallel -m:1 -nr:false
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed' }
}
& $sdkCommand build CadBatch.sln -c Release --no-restore -m:1 -nr:false -v minimal | Tee-Object -FilePath 'artifacts\verification\build.txt'
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$runtimeCommand = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $runtimeCommand)) { $runtimeCommand = $sdkCommand }
& $runtimeCommand tests\CadBatch.Tests\bin\Release\net8.0\CadBatch.Tests.dll | Tee-Object -FilePath 'artifacts\verification\tests.txt'
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
& (Join-Path $PSScriptRoot 'verify-net48-pdf.ps1')

& (Join-Path $PSScriptRoot 'verify-net48-wpf.ps1')

& (Join-Path $PSScriptRoot 'verify-net48-conflict.ps1')
