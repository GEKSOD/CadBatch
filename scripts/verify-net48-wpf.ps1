param([string]$AssemblyDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $AssemblyDirectory) { $AssemblyDirectory = Join-Path $projectRoot 'src\CadBatch.AutoCAD\bin\Release\net48' }
$outputDirectory = Join-Path $projectRoot 'artifacts\verification\net48-wpf'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $AssemblyDirectory -Filter '*.dll' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $outputDirectory -Force }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = Join-Path $projectRoot '.packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build\.NETFramework\v4.8'
$runner = Join-Path $outputDirectory 'WpfFrameworkSmoke.exe'
$arguments = @('/nologo','/target:exe','/platform:x64',('/out:' + $runner))
foreach ($name in @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','System.Xaml.dll','Facades\netstandard.dll')) { $arguments += '/reference:' + (Join-Path $refs $name) }
foreach ($name in @('CadBatch.Core.dll','CadBatch.AutoCAD.dll')) { $arguments += '/reference:' + (Join-Path $outputDirectory $name) }
$arguments += Join-Path $projectRoot 'tests\WpfFrameworkSmoke.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Framework WPF smoke compilation failed' }
$snapshot = Join-Path $outputDirectory 'interface.png'
$process = Start-Process -FilePath $runner -ArgumentList @(('"' + $snapshot + '"')) -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) {
    if (Test-Path -LiteralPath ($snapshot + '.error.txt')) { Get-Content -LiteralPath ($snapshot + '.error.txt') }
    throw 'Framework WPF smoke failed'
}
Get-Content -LiteralPath ($snapshot + '.txt')

$hostSnapshot = Join-Path $outputDirectory 'interface-no-app.png'
$hostProcess = Start-Process -FilePath $runner -ArgumentList @(('"' + $hostSnapshot + '"'),'--no-app') -WindowStyle Hidden -PassThru -Wait
if ($hostProcess.ExitCode -ne 0) {
    if (Test-Path -LiteralPath ($hostSnapshot + '.error.txt')) { Get-Content -LiteralPath ($hostSnapshot + '.error.txt') }
    throw 'Framework hosted WPF smoke failed (without Application.Current)'
}
Get-Content -LiteralPath ($hostSnapshot + '.txt')
